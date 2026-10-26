using Application.Abstractions;
using Application.Common;
using Application.Orders;
using Contracts.Admin.Operations;
using Contracts.Common;
using Domain.Entities.Support;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed partial class SupportService(IApplicationDbContext db, ICurrentActor actor, OrderService orders,
    Application.Marketplace.CustomerNotificationQueue? notifications = null)
{
    private bool Staff => actor.Role is "Administrator" or "Manager" or "Moderator";
    private bool OperationsStaff => actor.Role is "Administrator" or "Manager";
    private bool Responder => Staff || actor.Role == "Seller";
    // Product conversations belong to the product's active seller, never to the TREBA queue.
    private IQueryable<SupportTicket> Visible => actor.Role switch
    {
        "Administrator" or "Manager" => db.SupportTickets.Where(t => t.ProductId == null),
        "Moderator" => db.SupportTickets.Where(t => t.Kind == "CHAT" && t.ProductId == null),
        "Seller" => db.SupportTickets.Where(t => t.Kind == "QUESTION" && t.ProductId != null
            && db.Products.Any(p => p.Id == t.ProductId && db.Stores.Any(store => store.Id == p.StoreId && store.IsActive
                && db.Sellers.Any(s => s.Id == store.SellerId && s.UserId == actor.UserId && s.Status == SellerStatus.ACTIVE)))),
        "Customer" => db.SupportTickets.Where(t => t.CustomerId == actor.UserId),
        _ => db.SupportTickets.Where(t => false)
    };
    private IQueryable<SupportMessage> Messages(Guid id) => db.SupportMessages.AsNoTracking().Where(m => m.TicketId == id && (Staff || !m.Internal));
    private IQueryable<TicketRow> Rows(IQueryable<SupportTicket> query) =>
        from t in query
        join u in db.Users.AsNoTracking() on t.CustomerId equals u.Id
        join order in db.Orders.AsNoTracking() on t.OrderId equals order.Id into orderGroup
        from o in orderGroup.DefaultIfEmpty()
        join product in db.Products.AsNoTracking() on t.ProductId equals product.Id into productGroup
        from p in productGroup.DefaultIfEmpty()
        orderby t.UpdatedAt descending, t.Id
        select new TicketRow(t.Id, t.OrderId, o == null ? null : o.OrderNumber, t.CustomerId,
            (u.FirstName + " " + u.LastName).Trim(), t.Kind, t.Subject, t.Status, t.CreatedAt, t.UpdatedAt, t.Version,
            t.ProductId, p == null ? null : p.Name,
            db.SupportMessages.Where(m => m.TicketId == t.Id && (Staff || !m.Internal)).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Message).FirstOrDefault(),
            db.SupportMessages.Count(m => m.TicketId == t.Id && !m.Internal &&
                (Responder ? m.AuthorRole == "Customer" && (t.StaffReadAt == null || m.CreatedAt > t.StaffReadAt)
                    : (m.AuthorRole == "Administrator" || m.AuthorRole == "Manager" || m.AuthorRole == "Moderator" || m.AuthorRole == "Seller") && (t.CustomerReadAt == null || m.CreatedAt > t.CustomerReadAt))), p == null ? null : p.Slug, t.DeletedAt);

    public async Task<PagedResponse<TicketRow>> ListAsync(int page, string? status, string? kind, string? search, CancellationToken ct, string? view = null, Guid? productId = null, Guid? orderId = null)
    {
        OrderService.ValidatePage(page, 12);
        var query = Visible.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(view))
        {
            if (view is not ("open" or "overdue" or "unread")) throw new ArgumentException("Unknown ticket queue.");
            if (view == "unread") query = query.Where(t => db.SupportMessages.Any(m => m.TicketId == t.Id && !m.Internal &&
                (Responder ? m.AuthorRole == "Customer" && (t.StaffReadAt == null || m.CreatedAt > t.StaffReadAt)
                    : (m.AuthorRole == "Administrator" || m.AuthorRole == "Manager" || m.AuthorRole == "Moderator" || m.AuthorRole == "Seller") && (t.CustomerReadAt == null || m.CreatedAt > t.CustomerReadAt))));
            else query = query.Where(t => t.Status == "OPEN" || t.Status == "IN_PROGRESS" || t.Status == "APPROVED");
            if (view == "overdue")
            {
                var stale = DateTimeOffset.UtcNow.AddHours(-48);
                query = query.Where(t => t.CreatedAt < stale && !db.SupportMessages.Any(m => m.TicketId == t.Id && !m.Internal
                    && (t.ProductId != null ? m.AuthorRole == "Seller" : m.AuthorRole == "Administrator" || m.AuthorRole == "Manager" || m.AuthorRole == "Moderator") && m.CreatedAt >= stale));
            }
        }
        if (!string.IsNullOrWhiteSpace(status)) { ValidateStatus(status); query = query.Where(t => t.Status == status); }
        if (!string.IsNullOrWhiteSpace(kind)) { ValidateKind(kind); query = query.Where(t => t.Kind == kind); }
        if (productId.HasValue) query = query.Where(t => t.ProductId == productId);
        if (orderId.HasValue) query = query.Where(t => t.OrderId == orderId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Subject.ToLower().Contains(term)
                || db.Users.Any(u => u.Id == t.CustomerId && (u.FirstName.ToLower().Contains(term) || u.LastName.ToLower().Contains(term) || u.Email.ToLower().Contains(term)))
                || db.Products.Any(p => p.Id == t.ProductId && p.Name.ToLower().Contains(term))
                || db.Orders.Any(o => o.Id == t.OrderId && o.OrderNumber.ToLower().Contains(term)));
        }
        var count = await query.CountAsync(ct);
        var rows = await Rows(query.OrderByDescending(t => t.UpdatedAt).ThenBy(t => t.Id).Skip((page - 1) * 12).Take(12)).ToListAsync(ct);
        return new(rows, page, 12, count, (int)Math.Ceiling(count / 12d));
    }
    public async Task<TicketDetails?> DetailsAsync(Guid id, CancellationToken ct)
    {
        var row = await Rows(Visible.AsNoTracking().Where(t => t.Id == id)).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var page = await MessagesAsync(id, null, ct);
        return new(row, page.Messages, Responder ? Allowed(row.Kind, row.Status) : [], page.HasMore);
    }
    public async Task<TicketMessagePage> MessagesAsync(Guid id, Guid? before, CancellationToken ct)
    {
        if (!await Visible.AnyAsync(t => t.Id == id, ct)) throw new KeyNotFoundException();
        var query = Messages(id);
        if (before.HasValue)
        {
            var cursor = await query.SingleOrDefaultAsync(m => m.Id == before, ct) ?? throw new KeyNotFoundException();
            query = query.Where(m => m.CreatedAt < cursor.CreatedAt || m.CreatedAt == cursor.CreatedAt && m.Id.CompareTo(cursor.Id) < 0);
        }
        var messages = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(51).ToListAsync(ct);
        return new(messages.Take(50).Reverse().Select(m => new TicketMessageView(m.Id, m.AuthorName, m.AuthorRole, m.Message, m.Internal, m.CreatedAt)).ToList(), messages.Count > 50);
    }
    public async Task<TicketDetails> CreateAsync(CreateTicketRequest request, CancellationToken ct)
    {
        if (actor.Role != "Customer" && !OperationsStaff) throw new UnauthorizedAccessException();
        ValidateKind(request.Kind);
        OrderService.ValidateText(request.Subject, 200, "Subject");
        OrderService.ValidateText(request.Message, 4000, "Message");
        if (request.ClientRequestId == Guid.Empty) throw new ArgumentException("Invalid request identifier.");
        if (request.ClientRequestId is { } requestId && await db.SupportTickets.AnyAsync(t => t.Id == requestId, ct))
        {
            var existing = await DetailsAsync(requestId, ct) ?? throw new KeyNotFoundException();
            var first = await db.SupportMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == requestId && m.TicketId == requestId, ct);
            if (first is null || first.AuthorId != actor.UserId || first.Message != request.Message.Trim()) throw new BusinessRuleException("Request identifier already used.");
            return existing;
        }
        var customerId = actor.UserId ?? throw new UnauthorizedAccessException();
        if (request.OrderId.HasValue)
        {
            var order = await orders.VisibleOrders().SingleOrDefaultAsync(o => o.Id == request.OrderId, ct) ?? throw new KeyNotFoundException();
            if (!OperationsStaff && order.CustomerId != customerId) throw new KeyNotFoundException();
            customerId = order.CustomerId;
            if (request.Kind == "RETURN")
            {
                if (order.Status is not (OrderStatus.DELIVERED or OrderStatus.COMPLETED)) throw new BusinessRuleException("Returns are available for delivered or completed orders.");
                if (await db.SupportTickets.AnyAsync(t => t.OrderId == order.Id && t.Kind == "RETURN" && t.Status != "CLOSED" && t.Status != "REJECTED", ct))
                    throw new BusinessRuleException("This order already has an active return request.");
            }
        }
        if (request.Kind == "RETURN" && (!request.OrderId.HasValue || request.ProductId.HasValue)) throw new ArgumentException("A return requires an order.");
        if (request.Kind == "CHAT" && (request.OrderId.HasValue || request.ProductId.HasValue)) throw new ArgumentException("A personal chat cannot reference a product or order.");
        if (request.Kind == "QUESTION" && !request.ProductId.HasValue && !request.OrderId.HasValue) throw new ArgumentException("Choose a product or order for the question.");
        if (request.ProductId.HasValue && (request.OrderId.HasValue || !await db.Products.AnyAsync(p => p.Id == request.ProductId && p.Status == ProductStatus.ACTIVE, ct))) throw new KeyNotFoundException();
        if (OperationsStaff && !request.OrderId.HasValue) throw new ArgumentException("Customers start their personal and product chats.");
        if (request.Kind == "CHAT" || request.ProductId.HasValue)
        {
            var active = await Visible.SingleOrDefaultAsync(t => t.CustomerId == customerId && t.Kind == request.Kind && t.ProductId == request.ProductId && t.Status != "CLOSED", ct);
            if (active is not null)
            {
                await MessageAsync(active.Id, new(request.Message, false, request.ClientRequestId), ct);
                return (await DetailsAsync(active.Id, ct))!;
            }
        }
        var ticket = new SupportTicket { Id = request.ClientRequestId ?? Guid.NewGuid(), OrderId = request.OrderId, ProductId = request.ProductId, CustomerId = customerId, Kind = request.Kind, Subject = request.Subject.Trim() };
        db.SupportTickets.Add(ticket);
        var firstMessage = AddMessage(ticket.Id, request.Message, false, request.ClientRequestId);
        if (Responder && notifications is not null) await notifications.SupportAsync(ticket, firstMessage, ct);
        await db.SaveChangesAsync(ct);
        return (await DetailsAsync(ticket.Id, ct))!;
    }
    public async Task MessageAsync(Guid id, TicketMessageRequest request, CancellationToken ct)
    {
        OrderService.ValidateText(request.Message, 4000, "Message");
        var ticket = await Visible.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        if (request.Internal && !Staff) throw new ArgumentException("Only staff can add internal notes.");
        if (request.ClientMessageId == Guid.Empty) throw new ArgumentException("Invalid message identifier.");
        if (request.ClientMessageId is { } messageId && await db.SupportMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId, ct) is { } previous)
        {
            if (previous.TicketId == id && previous.AuthorId == actor.UserId && previous.Message == request.Message.Trim() && previous.Internal == request.Internal) return;
            throw new BusinessRuleException("Message identifier already used.");
        }
        if (ticket.Status == "CLOSED") throw new BusinessRuleException("Closed tickets cannot receive messages. Start a new conversation.");
        var message = AddMessage(id, request.Message, request.Internal, request.ClientMessageId);
        if (Responder && !request.Internal && notifications is not null)
            await notifications.SupportAsync(ticket, message, ct);
        ticket.Version = Guid.NewGuid(); ticket.UpdatedAt = DateTimeOffset.UtcNow;
        if (Responder && !request.Internal && ticket.Status == "OPEN") ticket.Status = "IN_PROGRESS";
        await db.SaveChangesAsync(ct);
    }
    public async Task MarkReadAsync(Guid id, ReadTicketRequest request, CancellationToken ct)
    {
        var ticket = await Visible.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        var message = await Messages(id).SingleOrDefaultAsync(m => m.Id == request.MessageId, ct) ?? throw new KeyNotFoundException();
        if (Responder) ticket.StaffReadAt = ticket.StaffReadAt > message.CreatedAt ? ticket.StaffReadAt : message.CreatedAt;
        else ticket.CustomerReadAt = ticket.CustomerReadAt > message.CreatedAt ? ticket.CustomerReadAt : message.CreatedAt;
        await db.SaveChangesAsync(ct);
    }
    public async Task ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken ct)
    {
        if (!Responder) throw new UnauthorizedAccessException();
        var ticket = await Visible.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        if (ticket.Version != request.ExpectedVersion) throw new BusinessRuleException("The ticket changed. Reload it before saving.");
        if (!Allowed(ticket.Kind, ticket.Status).Contains(request.Status)) throw new BusinessRuleException("This ticket status transition is not allowed.");
        if (ticket.Kind == "RETURN" && (request.Status == "APPROVED" || ticket.Status == "APPROVED" && request.Status == "CLOSED"))
        {
            var order = await db.Orders.SingleAsync(o => o.Id == ticket.OrderId, ct);
            var approve = request.Status == "APPROVED";
            if (approve ? order.Status is not (OrderStatus.DELIVERED or OrderStatus.COMPLETED) : order.Status != OrderStatus.RETURN_REQUESTED) throw new BusinessRuleException("The order status no longer permits this return operation.");
            await orders.StageTransitionAsync(order, approve ? OrderStatus.RETURN_REQUESTED : OrderStatus.RETURNED,
                approve ? "Return approved; awaiting receipt of goods." : "Returned goods received; stock restored. Payment refund must be handled separately.", ct);
        }
        var previous = ticket.Status; ticket.Status = request.Status; ticket.UpdatedAt = DateTimeOffset.UtcNow; ticket.Version = Guid.NewGuid();
        AddMessage(id, $"{previous} → {request.Status}", false); await db.SaveChangesAsync(ct);
    }
    private SupportMessage AddMessage(Guid id, string text, bool internalNote, Guid? messageId = null)
    {
        var message = new SupportMessage { Id = messageId ?? Guid.NewGuid(), TicketId = id,
            AuthorId = actor.UserId ?? throw new UnauthorizedAccessException(), AuthorName = actor.Name,
            AuthorRole = actor.Role, Message = text.Trim(), Internal = internalNote };
        db.SupportMessages.Add(message); return message;
    }
    private static IReadOnlyList<string> Allowed(string kind, string status) => (kind, status) switch
    {
        ("RETURN", "OPEN") => ["IN_PROGRESS", "APPROVED", "REJECTED"], ("RETURN", "IN_PROGRESS") => ["APPROVED", "REJECTED"],
        ("RETURN", "APPROVED" or "REJECTED") => ["CLOSED"], (_, "OPEN") => ["IN_PROGRESS", "CLOSED"], (_, "IN_PROGRESS") => ["CLOSED"], _ => []
    };
    private static void ValidateKind(string kind) { if (kind is not ("CHAT" or "QUESTION" or "RETURN")) throw new ArgumentException("Unknown ticket kind."); }
    private static void ValidateStatus(string status) { if (status is not ("OPEN" or "IN_PROGRESS" or "APPROVED" or "REJECTED" or "CLOSED")) throw new ArgumentException("Unknown ticket status."); }
}
