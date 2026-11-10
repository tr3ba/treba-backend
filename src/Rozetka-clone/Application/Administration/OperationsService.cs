using Application.Abstractions;
using Application.Orders;
using Contracts.Admin.Operations;
using Contracts.Common;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed class OperationsService(IApplicationDbContext db)
{
    public async Task<OperationsMetrics> MetricsAsync(int days, CancellationToken ct)
    {
        if (days is not (7 or 30 or 90))
            throw new ArgumentException("Period must be 7, 30 or 90 days.");

        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-days);
        var stale = now.AddHours(-48);
        var moderationStale = now.UtcDateTime.AddHours(-24);
        var periodOrders = db.Orders.AsNoTracking().Where(order => order.CreatedAt >= since);

        // These are placed-order amounts, not payment-provider revenue.
        var amount = await periodOrders
            .Where(order => order.Status != OrderStatus.CANCELLED && order.Status != OrderStatus.RETURNED)
            .SumAsync(order => (decimal?)order.TotalAmount, ct) ?? 0;

        var orderCount = await periodOrders.CountAsync(ct);
        var cancelledOrderCount = await db.Orders
            .CountAsync(order => order.CancelledAt >= since, ct);

        var openTicketCount = await db.SupportTickets.CountAsync(ticket =>
            ticket.ProductId == null
            && (ticket.Status == "OPEN" || ticket.Status == "IN_PROGRESS" || ticket.Status == "APPROVED"), ct);

        var overdueTicketCount = await db.SupportTickets.CountAsync(ticket =>
            ticket.ProductId == null
            && (ticket.Status == "OPEN" || ticket.Status == "IN_PROGRESS" || ticket.Status == "APPROVED")
            && ticket.CreatedAt < stale
            && !db.SupportMessages.Any(message =>
                message.TicketId == ticket.Id
                && !message.Internal
                && (message.AuthorRole == "Administrator"
                    || message.AuthorRole == "Manager"
                    || message.AuthorRole == "Moderator")
                && message.CreatedAt >= stale), ct);

        var activeProductsWithoutStock = await db.Products.CountAsync(product =>
            product.Status == ProductStatus.ACTIVE
            && !db.ProductVariants.Any(variant =>
                variant.ProductId == product.Id
                && variant.IsActive
                && variant.StockQuantity > 0), ct);

        var staleModerationCount = await db.Products.CountAsync(product =>
            product.Status == ProductStatus.PENDING_MODERATION
            && product.UpdatedAt < moderationStale, ct);

        return new(
            days,
            orderCount,
            cancelledOrderCount,
            amount,
            openTicketCount,
            overdueTicketCount,
            activeProductsWithoutStock,
            staleModerationCount);
    }

    public async Task<PagedResponse<CustomerRow>> CustomersAsync(int page, string? search, CancellationToken ct)
    {
        OrderService.ValidatePage(page, 12);

        var query = db.Users.AsNoTracking().Where(user =>
            !user.IsDeleted
            && (db.Orders.Any(order => order.CustomerId == user.Id)
                || db.Reviews.Any(review => review.UserId == user.Id)
                || db.SupportTickets.IgnoreQueryFilters().Any(ticket => ticket.CustomerId == user.Id)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(user =>
                user.Email.ToLower().Contains(term)
                || user.FirstName.ToLower().Contains(term)
                || user.LastName.ToLower().Contains(term));
        }

        var count = await query.CountAsync(ct);
        var skip = (page - 1) * 12;
        var users = await query
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.Id)
            .Skip(skip)
            .Take(12)
            .ToListAsync(ct);

        var customerIds = users.Select(user => user.Id).ToList();

        var orders = await db.Orders.AsNoTracking()
            .Where(order => customerIds.Contains(order.CustomerId))
            .Select(order => new
            {
                order.CustomerId,
                order.TotalAmount,
                order.Status,
                order.CreatedAt
            })
            .ToListAsync(ct);

        var reviewCounts = await db.Reviews.AsNoTracking()
            .Where(review => customerIds.Contains(review.UserId) && review.Status != ReviewStatus.DELETED)
            .GroupBy(review => review.UserId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, ct);

        var customerRows = users.Select(user =>
        {
            var customerOrders = orders.Where(order => order.CustomerId == user.Id).ToList();
            var completedOrders = customerOrders
                .Where(order => order.Status != OrderStatus.CANCELLED && order.Status != OrderStatus.RETURNED);
            var orderedAmount = completedOrders.Sum(order => order.TotalAmount);
            var lastOrderAt = customerOrders
                .Select(order => (DateTimeOffset?)order.CreatedAt)
                .Max();

            return new CustomerRow(
                user.Id,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.Email,
                customerOrders.Count,
                orderedAmount,
                reviewCounts.GetValueOrDefault(user.Id),
                lastOrderAt);
        }).ToList();

        return new(
            customerRows,
            page,
            12,
            count,
            (int)Math.Ceiling(count / 12d));
    }

    public async Task<CustomerDetails?> CustomerAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(customer => customer.Id == id && !customer.IsDeleted
                && (db.Orders.Any(o => o.CustomerId == customer.Id)
                    || db.Reviews.Any(r => r.UserId == customer.Id)
                    || db.SupportTickets.IgnoreQueryFilters().Any(t => t.CustomerId == customer.Id)), ct);

        if (user is null)
            return null;

        var customerOrders = db.Orders.AsNoTracking().Where(order => order.CustomerId == id);

        var orders = await customerOrders
            .OrderByDescending(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .Take(100)
            .ToListAsync(ct);

        var orderIds = orders.Select(order => order.Id).ToList();
        var items = await db.OrderItems.AsNoTracking()
            .Where(item => orderIds.Contains(item.OrderId))
            .ToListAsync(ct);

        var reviews = await (
            from review in db.Reviews.AsNoTracking()
            join product in db.Products.AsNoTracking() on review.ProductId equals product.Id
            where review.UserId == id && review.Status != ReviewStatus.DELETED
            orderby review.CreatedAt descending, review.Id
            select new CustomerReview(
                review.Id,
                product.Id,
                product.Name,
                review.Rating,
                review.Comment,
                review.Status.ToString(),
                review.CreatedAt)
        ).Take(100).ToListAsync(ct);

        var tickets = await (
            from ticket in db.SupportTickets.AsNoTracking()
            join order in db.Orders.AsNoTracking() on ticket.OrderId equals order.Id into orderGroup
            from order in orderGroup.DefaultIfEmpty()
            join product in db.Products.AsNoTracking() on ticket.ProductId equals product.Id into productGroup
            from product in productGroup.DefaultIfEmpty()
            where ticket.CustomerId == id && ticket.ProductId == null
            orderby ticket.UpdatedAt descending, ticket.Id
            select new TicketRow(
                ticket.Id,
                ticket.OrderId,
                order == null ? null : order.OrderNumber,
                ticket.CustomerId,
                (user.FirstName + " " + user.LastName).Trim(),
                ticket.Kind,
                ticket.Subject,
                ticket.Status,
                ticket.CreatedAt,
                ticket.UpdatedAt,
                ticket.Version,
                ticket.ProductId,
                product == null ? null : product.Name)
        ).Take(100).ToListAsync(ct);

        var amount = await customerOrders
            .Where(order => order.Status != OrderStatus.CANCELLED && order.Status != OrderStatus.RETURNED)
            .SumAsync(order => (decimal?)order.TotalAmount, ct) ?? 0;

        var orderCount = await customerOrders.CountAsync(ct);
        var reviewCount = await db.Reviews.CountAsync(
            review => review.UserId == id && review.Status != ReviewStatus.DELETED,
            ct);

        var summary = new CustomerRow(
            id,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.Email,
            orderCount,
            amount,
            reviewCount,
            orders.FirstOrDefault()?.CreatedAt);

        var orderRows = orders
            .Select(order => OrderService.Row(order, items.Where(item => item.OrderId == order.Id)))
            .ToList();

        return new CustomerDetails(summary, orderRows, reviews, tickets);
    }
}
