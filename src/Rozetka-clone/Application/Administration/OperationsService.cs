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
        var period = db.Orders.AsNoTracking().Where(o => o.CreatedAt >= since);
        // These are placed-order amounts, not payment-provider revenue.
        var amount = await period.Where(o => o.Status != OrderStatus.CANCELLED && o.Status != OrderStatus.RETURNED).SumAsync(o => (decimal?)o.TotalAmount, ct) ?? 0;
        return new(days, await period.CountAsync(ct), await db.Orders.CountAsync(o => o.CancelledAt >= since, ct), amount,
            await db.SupportTickets.CountAsync(t => t.ProductId == null && (t.Status == "OPEN" || t.Status == "IN_PROGRESS" || t.Status == "APPROVED"), ct),
            await db.SupportTickets.CountAsync(t => t.ProductId == null && (t.Status == "OPEN" || t.Status == "IN_PROGRESS" || t.Status == "APPROVED") && t.CreatedAt < stale
                && !db.SupportMessages.Any(m => m.TicketId == t.Id && !m.Internal && (m.AuthorRole == "Administrator" || m.AuthorRole == "Manager" || m.AuthorRole == "Moderator") && m.CreatedAt >= stale), ct),
            await db.Products.CountAsync(p => p.Status == ProductStatus.ACTIVE && !db.ProductVariants.Any(v => v.ProductId == p.Id && v.IsActive && v.StockQuantity > 0), ct),
            await db.Products.CountAsync(p => p.Status == ProductStatus.PENDING_MODERATION && p.UpdatedAt < moderationStale, ct));
    }
    public async Task<PagedResponse<CustomerRow>> CustomersAsync(int page, string? search, CancellationToken ct)
    {
        OrderService.ValidatePage(page, 12);
        var query = db.Users.AsNoTracking().Where(u => !u.IsDeleted && (db.Orders.Any(o => o.CustomerId == u.Id) || db.Reviews.Any(r => r.UserId == u.Id) || db.SupportTickets.Any(t => t.CustomerId == u.Id)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.ToLower().Contains(term) || u.FirstName.ToLower().Contains(term) || u.LastName.ToLower().Contains(term));
        }
        var count = await query.CountAsync(ct);
        var users = await query.OrderBy(u => u.LastName).ThenBy(u => u.Id).Skip((page - 1) * 12).Take(12).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.CustomerId)).Select(o => new { o.CustomerId, o.TotalAmount, o.Status, o.CreatedAt }).ToListAsync(ct);
        var reviews = await db.Reviews.AsNoTracking().Where(r => ids.Contains(r.UserId) && r.Status != ReviewStatus.DELETED).GroupBy(r => r.UserId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return new(users.Select(u => new CustomerRow(u.Id, $"{u.FirstName} {u.LastName}".Trim(), u.Email, orders.Count(o => o.CustomerId == u.Id),
            orders.Where(o => o.CustomerId == u.Id && o.Status != OrderStatus.CANCELLED && o.Status != OrderStatus.RETURNED).Sum(o => o.TotalAmount), reviews.GetValueOrDefault(u.Id),
            orders.Where(o => o.CustomerId == u.Id).Select(o => (DateTimeOffset?)o.CreatedAt).Max())).ToList(), page, 12, count, (int)Math.Ceiling(count / 12d));
    }
    public async Task<CustomerDetails?> CustomerAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);
        if (user is null)
            return null;
        var all = db.Orders.AsNoTracking().Where(o => o.CustomerId == id);
        var orders = await all.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(100).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var items = await db.OrderItems.AsNoTracking().Where(i => ids.Contains(i.OrderId)).ToListAsync(ct);
        var reviews = await (from r in db.Reviews.AsNoTracking()
                             join p in db.Products.AsNoTracking() on r.ProductId equals p.Id
                             where r.UserId == id && r.Status != ReviewStatus.DELETED
                             orderby r.CreatedAt descending, r.Id
                             select new CustomerReview(r.Id, p.Id, p.Name, r.Rating, r.Comment, r.Status.ToString(), r.CreatedAt)).Take(100).ToListAsync(ct);
        var tickets = await (from t in db.SupportTickets.AsNoTracking()
                             join order in db.Orders.AsNoTracking() on t.OrderId equals order.Id into orderGroup
                             from o in orderGroup.DefaultIfEmpty()
                             join product in db.Products.AsNoTracking() on t.ProductId equals product.Id into productGroup
                             from p in productGroup.DefaultIfEmpty()
                             where t.CustomerId == id && t.ProductId == null
                             orderby t.UpdatedAt descending, t.Id
                             select new TicketRow(t.Id, t.OrderId, o == null ? null : o.OrderNumber, t.CustomerId, (user.FirstName + " " + user.LastName).Trim(), t.Kind, t.Subject, t.Status, t.CreatedAt, t.UpdatedAt, t.Version, t.ProductId, p == null ? null : p.Name)).Take(100).ToListAsync(ct);
        var amount = await all.Where(o => o.Status != OrderStatus.CANCELLED && o.Status != OrderStatus.RETURNED).SumAsync(o => (decimal?)o.TotalAmount, ct) ?? 0;
        return new(new(id, $"{user.FirstName} {user.LastName}".Trim(), user.Email, await all.CountAsync(ct), amount, await db.Reviews.CountAsync(r => r.UserId == id && r.Status != ReviewStatus.DELETED, ct), orders.FirstOrDefault()?.CreatedAt),
            orders.Select(o => OrderService.Row(o, items.Where(i => i.OrderId == o.Id))).ToList(), reviews, tickets);
    }
}
