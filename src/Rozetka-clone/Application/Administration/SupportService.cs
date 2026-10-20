using Application.Abstractions;
using Application.Common;
using Application.Orders;
using Contracts.Admin.Operations;
using Contracts.Common;
using Domain.Entities.Support;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed class SupportService(IApplicationDbContext db, ICurrentActor actor, OrderService orders)
{
    private bool Staff => actor.Role is "Administrator" or "Manager";
    private IQueryable<SupportTicket> Visible => Staff ? db.SupportTickets : db.SupportTickets.Where(t => t.CustomerId == actor.UserId);
    public async Task<PagedResponse<TicketRow>> ListAsync(int page, string? status, string? kind, string? search, CancellationToken ct, string? view = null)
    {
        OrderService.ValidatePage(page, 12);
        var query = Visible.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(view))
        {
            if (view is not ("open" or "overdue"))
                throw new ArgumentException("Unknown ticket queue.");
            query = query.Where(t => t.Status == "OPEN" || t.Status == "IN_PROGRESS" || t.Status == "APPROVED");
            if (view == "overdue")
            {
                var stale = DateTimeOffset.UtcNow.AddHours(-48);
                query = query.Where(t => t.CreatedAt < stale && !db.SupportMessages.Any(m => m.TicketId == t.Id
                    && (m.AuthorRole == "Administrator" || m.AuthorRole == "Manager") && m.CreatedAt >= stale));
            }
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            ValidateStatus(status);
            query = query.Where(t => t.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(kind))
        {
            ValidateKind(kind);
            query = query.Where(t => t.Kind == kind);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Subject.ToLower().Contains(term) || db.Orders.Any(o => o.Id == t.OrderId && (o.OrderNumber.ToLower().Contains(term) || o.RecipientName.ToLower().Contains(term))));
        }
        var count = await query.CountAsync(ct);
        var rows = await (from t in query
                          join o in db.Orders.AsNoTracking() on t.OrderId equals o.Id
                          orderby t.UpdatedAt descending, t.Id
                          select new TicketRow(t.Id, o.Id, o.OrderNumber, t.CustomerId, o.RecipientName, t.Kind, t.Subject, t.Status, t.CreatedAt, t.UpdatedAt, t.Version))
                          .Skip((page - 1) * 12).Take(12).ToListAsync(ct);
        return new(rows, page, 12, count, (int)Math.Ceiling(count / 12d));
    }
    public async Task<TicketDetails?> DetailsAsync(Guid id, CancellationToken ct)
    {
        var row = await (from t in Visible.AsNoTracking()
                         join o in db.Orders.AsNoTracking() on t.OrderId equals o.Id
                         where t.Id == id
                         select new TicketRow(t.Id, o.Id, o.OrderNumber, t.CustomerId, o.RecipientName, t.Kind, t.Subject, t.Status, t.CreatedAt, t.UpdatedAt, t.Version)).SingleOrDefaultAsync(ct);
        if (row is null)
            return null;
        var messages = await db.SupportMessages.AsNoTracking().Where(m => m.TicketId == id && (Staff || !m.Internal))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(200).ToListAsync(ct);
        return new(row, messages.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Select(m => new TicketMessageView(m.Id, m.AuthorName, m.AuthorRole, m.Message, m.Internal, m.CreatedAt)).ToList(), Staff ? Allowed(row.Kind, row.Status) : []);
    }
    public async Task<TicketDetails> CreateAsync(CreateTicketRequest request, CancellationToken ct)
    {
        ValidateKind(request.Kind);
        OrderService.ValidateText(request.Subject, 200, "Subject");
        OrderService.ValidateText(request.Message, 4000, "Message");
        var order = await orders.VisibleOrders().SingleOrDefaultAsync(o => o.Id == request.OrderId, ct) ?? throw new KeyNotFoundException();
        if (!Staff && order.CustomerId != actor.UserId)
            throw new KeyNotFoundException();
        if (request.Kind == "RETURN")
        {
            if (order.Status is not (OrderStatus.DELIVERED or OrderStatus.COMPLETED))
                throw new BusinessRuleException("Returns are available for delivered or completed orders.");
            if (await db.SupportTickets.AnyAsync(t => t.OrderId == order.Id && t.Kind == "RETURN" && (t.Status == "OPEN" || t.Status == "IN_PROGRESS" || t.Status == "APPROVED"), ct))
                throw new BusinessRuleException("This order already has an active return request.");
        }
        var ticket = new SupportTicket { OrderId = order.Id, CustomerId = order.CustomerId, Kind = request.Kind, Subject = request.Subject.Trim() };
        db.SupportTickets.Add(ticket);
        AddMessage(ticket.Id, request.Message, false);
        await db.SaveChangesAsync(ct);
        return (await DetailsAsync(ticket.Id, ct))!;
    }
    public async Task MessageAsync(Guid id, TicketMessageRequest request, CancellationToken ct)
    {
        OrderService.ValidateText(request.Message, 4000, "Message");
        var ticket = await Visible.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        if (ticket.Status == "CLOSED")
            throw new BusinessRuleException("Closed tickets cannot receive messages.");
        if (request.Internal && !Staff)
            throw new ArgumentException("Only staff can add internal notes.");
        AddMessage(id, request.Message, request.Internal);
        ticket.Version = Guid.NewGuid();
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    public async Task ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken ct)
    {
        if (!Staff)
            throw new UnauthorizedAccessException();
        var ticket = await db.SupportTickets.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        if (ticket.Version != request.ExpectedVersion)
            throw new BusinessRuleException("The ticket changed. Reload it before saving.");
        if (!Allowed(ticket.Kind, ticket.Status).Contains(request.Status))
            throw new BusinessRuleException("This ticket status transition is not allowed.");
        if (ticket.Kind == "RETURN" && (request.Status == "APPROVED" || ticket.Status == "APPROVED" && request.Status == "CLOSED"))
        {
            var order = await db.Orders.SingleAsync(o => o.Id == ticket.OrderId, ct);
            var approve = request.Status == "APPROVED";
            if (approve ? order.Status is not (OrderStatus.DELIVERED or OrderStatus.COMPLETED) : order.Status != OrderStatus.RETURN_REQUESTED)
                throw new BusinessRuleException("The order status no longer permits this return operation.");
            await orders.StageTransitionAsync(order, approve ? OrderStatus.RETURN_REQUESTED : OrderStatus.RETURNED,
                approve ? "Return approved; awaiting receipt of goods." : "Returned goods received; stock restored. Payment refund must be handled separately.", ct);
        }
        var previous = ticket.Status;
        ticket.Status = request.Status;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        ticket.Version = Guid.NewGuid();
        AddMessage(id, $"{previous} → {request.Status}", false);
        await db.SaveChangesAsync(ct);
    }
    private void AddMessage(Guid id, string text, bool internalNote) => db.SupportMessages.Add(new()
    {
        TicketId = id,
        AuthorId = actor.UserId ?? throw new UnauthorizedAccessException(),
        AuthorName = actor.Name,
        AuthorRole = actor.Role,
        Message = text.Trim(),
        Internal = internalNote
    });
    private static IReadOnlyList<string> Allowed(string kind, string status) => (kind, status) switch
    {
        ("RETURN", "OPEN") => ["IN_PROGRESS", "APPROVED", "REJECTED"],
        ("RETURN", "IN_PROGRESS") => ["APPROVED", "REJECTED"],
        ("RETURN", "APPROVED" or "REJECTED") => ["CLOSED"],
        (_, "OPEN") => ["IN_PROGRESS", "CLOSED"],
        (_, "IN_PROGRESS") => ["CLOSED"],
        _ => []
    };
    private static void ValidateKind(string kind)
    {
        if (kind is not ("QUESTION" or "RETURN"))
            throw new ArgumentException("Unknown ticket kind.");
    }
    private static void ValidateStatus(string status)
    {
        if (status is not ("OPEN" or "IN_PROGRESS" or "APPROVED" or "REJECTED" or "CLOSED"))
            throw new ArgumentException("Unknown ticket status.");
    }
}
