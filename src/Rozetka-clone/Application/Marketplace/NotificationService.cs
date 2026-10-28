using Application.Abstractions;
using Contracts.Marketplace;
using Domain.Entities;
using Domain.Entities.Users;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Marketplace;

public sealed class NotificationService(IApplicationDbContext db, ICurrentActor actor)
{
    private Guid UserId => actor.UserId ?? throw new UnauthorizedAccessException();
    public async Task<NotificationFeed> FeedAsync(CancellationToken ct)
    {
        var userId = UserId;
        var staff = actor.Role is Roles.Administrator or Roles.Manager;
        var supportStaff = staff || actor.Role == Roles.Moderator;
        var seller = actor.Role == Roles.Seller;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var productIds = db.Products.Where(p => db.Stores.Any(s => s.Id == p.StoreId && s.IsActive && db.Sellers.Any(v => v.Id == s.SellerId && v.UserId == userId && v.Status == SellerStatus.ACTIVE))).Select(p => p.Id);
        var result = new List<MarketplaceNotification>();
        var messages = await (from m in db.SupportMessages.AsNoTracking()
            join t in db.SupportTickets.AsNoTracking() on m.TicketId equals t.Id
            where !m.Internal && m.AuthorId != userId && m.CreatedAt >= cutoff
                && (t.CustomerId == userId || m.AuthorId == t.CustomerId &&
                    ((staff || actor.Role == Roles.Moderator && t.Kind == "CHAT") && t.ProductId == null
                        || seller && t.Kind == "QUESTION" && t.ProductId != null && productIds.Contains(t.ProductId.Value)))
            orderby m.CreatedAt descending, m.Id
            select new { m.Id, m.CreatedAt, m.Message, t.CustomerId, TicketId = t.Id, t.Subject, t.ProductId }).Take(100).ToListAsync(ct);
        result.AddRange(messages.Select(m => new MarketplaceNotification("message:" + m.Id, m.ProductId == null ? "support" : "question",
            m.ProductId == null ? "Нове повідомлення підтримки" : "Нове повідомлення про товар", Short(m.Subject + ": " + m.Message),
            m.CustomerId == userId ? "shop" : "admin", m.CustomerId == userId ? "/account/messages?ticket=" + m.TicketId : "/support/" + m.TicketId, m.CreatedAt)));
        if (staff || seller)
        {
            var orders = db.Orders.AsNoTracking().Where(o => o.CreatedAt >= cutoff);
            if (seller) orders = orders.Where(o => db.OrderItems.Any(i => i.OrderId == o.Id && db.Sellers.Any(s => s.Id == i.SellerId && s.UserId == userId && s.Status == SellerStatus.ACTIVE)));
            var rows = await orders.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(60).Select(o => new { o.Id, o.OrderNumber, o.CreatedAt }).ToListAsync(ct);
            result.AddRange(rows.Select(o => new MarketplaceNotification("order:" + o.Id, "order", "Нове замовлення", o.OrderNumber, "admin", "/orders/" + o.Id, o.CreatedAt)));
        }
        var updates = await (from h in db.OrderStatusHistories.AsNoTracking() join o in db.Orders on h.OrderId equals o.Id
            where o.CustomerId == userId && h.CreatedAt >= cutoff orderby h.CreatedAt descending, h.Id
            select new { h.Id, h.OrderId, h.CreatedAt, h.NewStatus, o.OrderNumber }).Take(60).ToListAsync(ct);
        result.AddRange(updates.Select(h => new MarketplaceNotification("order-status:" + h.Id, "order-status",
            CustomerNotificationQueue.OrderTitle(h.NewStatus.ToString()),
            CustomerNotificationQueue.OrderText(h.OrderNumber, h.NewStatus.ToString()), "shop", "/account/orders/" + h.OrderId, h.CreatedAt)));
        if (supportStaff || seller)
        {
            var cutoffUtc = cutoff.UtcDateTime;
            var query = db.Products.AsNoTracking().Where(p => p.UpdatedAt >= cutoffUtc);
            query = seller ? query.Where(p => productIds.Contains(p.Id) && (p.Status == ProductStatus.ACTIVE || p.Status == ProductStatus.REJECTED))
                : query.Where(p => p.Status == ProductStatus.PENDING_MODERATION);
            var products = await query.OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Id).Take(60).Select(p => new { p.Id, p.Name, p.Status, p.UpdatedAt }).ToListAsync(ct);
            result.AddRange(products.Select(p => new MarketplaceNotification("moderation:" + p.Id + ":" + p.UpdatedAt.Ticks, "moderation",
                p.Status == ProductStatus.PENDING_MODERATION ? "Товар очікує перевірки" : p.Status == ProductStatus.ACTIVE ? "Товар опубліковано" : "Товар відхилено",
                Short(p.Name), "admin", seller ? "/products/" + p.Id + "/edit" : "/moderation", new DateTimeOffset(DateTime.SpecifyKind(p.UpdatedAt, DateTimeKind.Utc)))));
        }
        var applications = db.SellerApplications.AsNoTracking().Where(a => a.UpdatedAt >= cutoff && (a.UserId == userId || staff && a.Status == "pending"));
        var requests = await applications.OrderByDescending(a => a.UpdatedAt).ThenBy(a => a.Id).Take(40).ToListAsync(ct);
        result.AddRange(requests.Select(a => new MarketplaceNotification("application:" + a.Id + ":" + a.Version, "application",
            a.Status == "pending" ? "Заявка продавця на розгляді" : a.Status == "approved" ? "Заявку продавця схвалено" : "Заявку продавця відхилено",
            Short(a.CompanyName + (a.ReviewNote is null ? "" : ": " + a.ReviewNote)),
            a.UserId == userId ? "shop" : "admin", a.UserId == userId ? "/sell#apply" : "/sellers/applications", a.UpdatedAt)));
        var feed = result.DistinctBy(x => x.Key).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Key).Take(300).ToList();
        var keys = feed.Select(x => x.Key).ToArray();
        var read = (await db.NotificationReceipts.AsNoTracking().Where(x => x.UserId == userId && keys.Contains(x.EventKey)).Select(x => x.EventKey).ToListAsync(ct)).ToHashSet();
        return new(feed.Select(x => x with { Read = read.Contains(x.Key) }).ToList(), feed.Count(x => !read.Contains(x.Key)));
    }

    public async Task ReadAsync(string[] keys, CancellationToken ct)
    {
        if (keys is null || keys.Length > 300 || keys.Any(x => x is null || x.Length > 120)) throw new ArgumentException("Invalid notification keys.");
        var visible = (await FeedAsync(ct)).Items.Select(x => x.Key).ToHashSet();
        if (keys.Any(x => !visible.Contains(x))) throw new KeyNotFoundException();
        var distinct = keys.Distinct().ToArray();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var read = await db.NotificationReceipts.Where(x => x.UserId == UserId && distinct.Contains(x.EventKey)).Select(x => x.EventKey).ToListAsync(ct);
            var added = distinct.Except(read).Select(key => db.NotificationReceipts.Add(new() { UserId = UserId, EventKey = key })).ToList();
            if (added.Count == 0) return;
            try { await db.SaveChangesAsync(ct); return; }
            catch (DbUpdateException) when (attempt < 2)
            {
                foreach (var entry in added) entry.State = EntityState.Detached;
                // Another tab may mark the same events read concurrently; retry against the persisted receipts.
            }
        }
    }
    private static string Short(string text) => text.Length > 200 ? text[..200] + "…" : text;
}
