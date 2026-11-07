using Application.Abstractions;
using Application.Common;
using Application.Orders;
using Contracts.Admin.Operations;
using Contracts.Common;
using Domain.Entities.Support;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed partial class SupportService(
    IApplicationDbContext db,
    ICurrentActor actor,
    OrderService orders,
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

    private IQueryable<SupportMessage> Messages(Guid id) => db.SupportMessages
        .AsNoTracking()
        .Where(message => message.TicketId == id && (Staff || !message.Internal));

    private IQueryable<TicketRow> Rows(IQueryable<SupportTicket> query) =>
        from t in query
        join u in db.Users.AsNoTracking() on t.CustomerId equals u.Id
        join order in db.Orders.AsNoTracking() on t.OrderId equals order.Id into orderGroup
        from o in orderGroup.DefaultIfEmpty()
        join product in db.Products.AsNoTracking() on t.ProductId equals product.Id into productGroup
        from p in productGroup.DefaultIfEmpty()
        orderby t.UpdatedAt descending, t.Id
        select new TicketRow(
            t.Id,
            t.OrderId,
            o == null ? null : o.OrderNumber,
            t.CustomerId,
            (u.FirstName + " " + u.LastName).Trim(),
            t.Kind,
            t.Subject,
            t.Status,
            t.CreatedAt,
            t.UpdatedAt,
            t.Version,
            t.ProductId,
            p == null ? null : p.Name,
            db.SupportMessages
                .Where(message => message.TicketId == t.Id && (Staff || !message.Internal))
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .Select(message => message.Message)
                .FirstOrDefault(),
            db.SupportMessages.Count(message =>
                message.TicketId == t.Id
                && !message.Internal
                && (Responder
                    ? message.AuthorRole == "Customer"
                        && (t.StaffReadAt == null || message.CreatedAt > t.StaffReadAt)
                    : (message.AuthorRole == "Administrator"
                        || message.AuthorRole == "Manager"
                        || message.AuthorRole == "Moderator"
                        || message.AuthorRole == "Seller")
                        && (t.CustomerReadAt == null || message.CreatedAt > t.CustomerReadAt))),
            p == null ? null : p.Slug,
            t.DeletedAt);

    public async Task<PagedResponse<TicketRow>> ListAsync(
        int page,
        string? status,
        string? kind,
        string? search,
        CancellationToken ct,
        string? view = null,
        Guid? productId = null,
        Guid? orderId = null)
    {
        OrderService.ValidatePage(page, 12);

        var query = Visible.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(view))
        {
            if (view is not ("open" or "overdue" or "unread"))
                throw new ArgumentException("Unknown ticket queue.");

            if (view == "unread")
            {
                query = query.Where(ticket => db.SupportMessages.Any(message =>
                    message.TicketId == ticket.Id
                    && !message.Internal
                    && (Responder
                        ? message.AuthorRole == "Customer"
                            && (ticket.StaffReadAt == null || message.CreatedAt > ticket.StaffReadAt)
                        : (message.AuthorRole == "Administrator"
                            || message.AuthorRole == "Manager"
                            || message.AuthorRole == "Moderator"
                            || message.AuthorRole == "Seller")
                            && (ticket.CustomerReadAt == null || message.CreatedAt > ticket.CustomerReadAt))));
            }
            else
            {
                query = query.Where(ticket =>
                    ticket.Status == "OPEN"
                    || ticket.Status == "IN_PROGRESS"
                    || ticket.Status == "APPROVED");
            }

            if (view == "overdue")
            {
                var stale = DateTimeOffset.UtcNow.AddHours(-48);
                query = query.Where(ticket =>
                    ticket.CreatedAt < stale
                    && !db.SupportMessages.Any(message =>
                        message.TicketId == ticket.Id
                        && !message.Internal
                        && (ticket.ProductId != null
                            ? message.AuthorRole == "Seller"
                            : message.AuthorRole == "Administrator"
                                || message.AuthorRole == "Manager"
                                || message.AuthorRole == "Moderator")
                        && message.CreatedAt >= stale));
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

        if (productId.HasValue)
            query = query.Where(t => t.ProductId == productId);

        if (orderId.HasValue)
            query = query.Where(t => t.OrderId == orderId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Subject.ToLower().Contains(term)
                || db.Users.Any(user => user.Id == t.CustomerId
                    && (user.FirstName.ToLower().Contains(term)
                        || user.LastName.ToLower().Contains(term)
                        || user.Email.ToLower().Contains(term)))
                || db.Products.Any(p => p.Id == t.ProductId && p.Name.ToLower().Contains(term))
                || db.Orders.Any(o => o.Id == t.OrderId && o.OrderNumber.ToLower().Contains(term)));
        }

        var count = await query.CountAsync(ct);
        var skip = (page - 1) * 12;
        var rows = await Rows(query
            .OrderByDescending(ticket => ticket.UpdatedAt)
            .ThenBy(ticket => ticket.Id)
            .Skip(skip)
            .Take(12))
            .ToListAsync(ct);

        return new(rows, page, 12, count, (int)Math.Ceiling(count / 12d));
    }

    public async Task<TicketDetails?> DetailsAsync(Guid id, CancellationToken ct)
    {
        var row = await Rows(Visible.AsNoTracking().Where(t => t.Id == id)).SingleOrDefaultAsync(ct);

        if (row is null)
            return null;

        var page = await MessagesAsync(id, null, ct);
        return new(row, page.Messages, Responder ? Allowed(row.Kind, row.Status) : [], page.HasMore);
    }

    public async Task<TicketMessagePage> MessagesAsync(Guid id, Guid? before, CancellationToken ct)
    {
        if (!await Visible.AnyAsync(t => t.Id == id, ct))
            throw new KeyNotFoundException();

        var query = Messages(id);

        if (before.HasValue)
        {
            var cursor = await query.SingleOrDefaultAsync(message => message.Id == before, ct)
                ?? throw new KeyNotFoundException();

            query = query.Where(message =>
                message.CreatedAt < cursor.CreatedAt
                || message.CreatedAt == cursor.CreatedAt && message.Id.CompareTo(cursor.Id) < 0);
        }

        var messages = await query
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(51)
            .ToListAsync(ct);
        var messageViews = messages.Take(50)
            .Reverse()
            .Select(message => new TicketMessageView(
                message.Id,
                message.AuthorName,
                message.AuthorRole,
                message.Message,
                message.Internal,
                message.CreatedAt))
            .ToList();

        return new(messageViews, messages.Count > 50);
    }

    public async Task<TicketDetails> CreateAsync(CreateTicketRequest request, CancellationToken ct)
    {
        if (actor.Role != "Customer" && !OperationsStaff)
            throw new UnauthorizedAccessException();

        ValidateKind(request.Kind);
        OrderService.ValidateText(request.Subject, 200, "Subject");
        OrderService.ValidateText(request.Message, 4000, "Message");

        if (request.ClientRequestId == Guid.Empty)
            throw new ArgumentException("Invalid request identifier.");

        if (request.ClientRequestId is { } requestId && await db.SupportTickets.AnyAsync(t => t.Id == requestId, ct))
        {
            var existing = await DetailsAsync(requestId, ct) ?? throw new KeyNotFoundException();
            var first = await db.SupportMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == requestId && m.TicketId == requestId, ct);

            if (first is null || first.AuthorId != actor.UserId || first.Message != request.Message.Trim())
                throw new BusinessRuleException("Request identifier already used.");

            return existing;
        }

        var customerId = actor.UserId ?? throw new UnauthorizedAccessException();

        if (request.OrderId.HasValue)
        {
            var order = await orders.VisibleOrders()
                .SingleOrDefaultAsync(o => o.Id == request.OrderId, ct)
                ?? throw new KeyNotFoundException();

            if (!OperationsStaff && order.CustomerId != customerId)
                throw new KeyNotFoundException();

            customerId = order.CustomerId;

            if (request.Kind == "RETURN")
            {
                if (order.Status is not (OrderStatus.DELIVERED or OrderStatus.COMPLETED))
                    throw new BusinessRuleException("Returns are available for delivered or completed orders.");

                var hasActiveReturn = await db.SupportTickets.AnyAsync(ticket =>
                    ticket.OrderId == order.Id
                    && ticket.Kind == "RETURN"
                    && ticket.Status != "CLOSED"
                    && ticket.Status != "REJECTED", ct);

                if (hasActiveReturn)
                    throw new BusinessRuleException("This order already has an active return request.");
            }
        }

        if (request.Kind == "RETURN" && (!request.OrderId.HasValue || request.ProductId.HasValue))
            throw new ArgumentException("A return requires an order.");

        if (request.Kind == "CHAT" && (request.OrderId.HasValue || request.ProductId.HasValue))
            throw new ArgumentException("A personal chat cannot reference a product or order.");

        if (request.Kind == "QUESTION" && !request.ProductId.HasValue && !request.OrderId.HasValue)
            throw new ArgumentException("Choose a product or order for the question.");

        if (request.ProductId.HasValue)
        {
            if (request.OrderId.HasValue)
                throw new KeyNotFoundException();

            var productIsActive = await db.Products.AnyAsync(
                product => product.Id == request.ProductId && product.Status == ProductStatus.ACTIVE,
                ct);

            if (!productIsActive)
                throw new KeyNotFoundException();
        }

        if (OperationsStaff && !request.OrderId.HasValue)
            throw new ArgumentException("Customers start their personal and product chats.");

        if (request.Kind == "CHAT" || request.ProductId.HasValue)
        {
            var active = await Visible.SingleOrDefaultAsync(ticket =>
                ticket.CustomerId == customerId
                && ticket.Kind == request.Kind
                && ticket.ProductId == request.ProductId
                && ticket.Status != "CLOSED", ct);

            if (active is not null)
            {
                await MessageAsync(active.Id, new(request.Message, false, request.ClientRequestId), ct);
                return (await DetailsAsync(active.Id, ct))!;
            }
        }

        var ticket = new SupportTicket
        {
            Id = request.ClientRequestId ?? Guid.NewGuid(),
            OrderId = request.OrderId,
            ProductId = request.ProductId,
            CustomerId = customerId,
            Kind = request.Kind,
            Subject = request.Subject.Trim()
        };
        db.SupportTickets.Add(ticket);

        var firstMessage = AddMessage(ticket.Id, request.Message, false, request.ClientRequestId);

        if (Responder && notifications is not null)
            await notifications.SupportAsync(ticket, firstMessage, ct);

        await db.SaveChangesAsync(ct);
        return (await DetailsAsync(ticket.Id, ct))!;
    }

    public async Task MessageAsync(Guid id, TicketMessageRequest request, CancellationToken ct)
    {
        OrderService.ValidateText(request.Message, 4000, "Message");
        var ticket = await Visible.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();

        if (request.Internal && !Staff)
            throw new ArgumentException("Only staff can add internal notes.");

        if (request.ClientMessageId == Guid.Empty)
            throw new ArgumentException("Invalid message identifier.");

        if (request.ClientMessageId is { } messageId)
        {
            var previous = await db.SupportMessages.AsNoTracking()
                .SingleOrDefaultAsync(message => message.Id == messageId, ct);

            if (previous is not null)
            {
                var isSameMessage = previous.TicketId == id
                    && previous.AuthorId == actor.UserId
                    && previous.Message == request.Message.Trim()
                    && previous.Internal == request.Internal;

                if (isSameMessage)
                    return;

                throw new BusinessRuleException("Message identifier already used.");
            }
        }

        if (ticket.Status == "CLOSED")
            throw new BusinessRuleException("Closed tickets cannot receive messages. Start a new conversation.");

        var message = AddMessage(id, request.Message, request.Internal, request.ClientMessageId);

        if (Responder && !request.Internal && notifications is not null)
            await notifications.SupportAsync(ticket, message, ct);

        ticket.Version = Guid.NewGuid();
        ticket.UpdatedAt = DateTimeOffset.UtcNow;

        if (Responder && !request.Internal && ticket.Status == "OPEN")
            ticket.Status = "IN_PROGRESS";
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkReadAsync(Guid id, ReadTicketRequest request, CancellationToken ct)
    {
        var ticket = await Visible.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();
        var message = await Messages(id).SingleOrDefaultAsync(m => m.Id == request.MessageId, ct) ?? throw new KeyNotFoundException();

        if (Responder)
            ticket.StaffReadAt = ticket.StaffReadAt > message.CreatedAt ? ticket.StaffReadAt : message.CreatedAt;
        else
            ticket.CustomerReadAt = ticket.CustomerReadAt > message.CreatedAt ? ticket.CustomerReadAt : message.CreatedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken ct)
    {
        if (!Responder)
            throw new UnauthorizedAccessException();

        var ticket = await Visible.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new KeyNotFoundException();

        if (ticket.Version != request.ExpectedVersion)
            throw new BusinessRuleException("The ticket changed. Reload it before saving.");

        if (!Allowed(ticket.Kind, ticket.Status).Contains(request.Status))
            throw new BusinessRuleException("This ticket status transition is not allowed.");

        var isReturnApproval = request.Status == "APPROVED"
            || ticket.Status == "APPROVED" && request.Status == "CLOSED";

        if (ticket.Kind == "RETURN" && isReturnApproval)
        {
            var order = await db.Orders.SingleAsync(o => o.Id == ticket.OrderId, ct);
            var approve = request.Status == "APPROVED";

            var orderStatusDoesNotAllowReturnAction = approve
                ? order.Status is not (OrderStatus.DELIVERED or OrderStatus.COMPLETED)
                : order.Status != OrderStatus.RETURN_REQUESTED;

            if (orderStatusDoesNotAllowReturnAction)
                throw new BusinessRuleException("The order status no longer permits this return operation.");

            var nextOrderStatus = approve ? OrderStatus.RETURN_REQUESTED : OrderStatus.RETURNED;
            var transitionComment = approve
                ? "Return approved; awaiting receipt of goods."
                : "Returned goods received; stock restored. Payment refund must be handled separately.";

            await orders.StageTransitionAsync(order, nextOrderStatus, transitionComment, ct);
        }

        var previous = ticket.Status;
        ticket.Status = request.Status;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        ticket.Version = Guid.NewGuid();
        AddMessage(id, $"{previous} → {request.Status}", false);
        await db.SaveChangesAsync(ct);
    }

    private SupportMessage AddMessage(Guid id, string text, bool internalNote, Guid? messageId = null)
    {
        var message = new SupportMessage
        {
            Id = messageId ?? Guid.NewGuid(),
            TicketId = id,
            AuthorId = actor.UserId ?? throw new UnauthorizedAccessException(),
            AuthorName = actor.Name,
            AuthorRole = actor.Role,
            Message = text.Trim(),
            Internal = internalNote
        };
        db.SupportMessages.Add(message);
        return message;
    }

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
        if (kind is not ("CHAT" or "QUESTION" or "RETURN"))
            throw new ArgumentException("Unknown ticket kind.");
    }

    private static void ValidateStatus(string status)
    {
        if (status is not ("OPEN" or "IN_PROGRESS" or "APPROVED" or "REJECTED" or "CLOSED"))
            throw new ArgumentException("Unknown ticket status.");
    }
}
