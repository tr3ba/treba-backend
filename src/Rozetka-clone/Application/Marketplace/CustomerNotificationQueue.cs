using Application.Abstractions;
using Contracts.Marketplace;
using Domain.Entities;
using Domain.Entities.Orders;
using Domain.Entities.Support;
using Microsoft.EntityFrameworkCore;

namespace Application.Marketplace;

public interface INotificationChannels
{
    bool EmailAvailable { get; }
    bool TelegramAvailable { get; }
}

public sealed class CustomerNotificationSettingsService(IApplicationDbContext db, ICurrentActor actor, INotificationChannels channels)
{
    private Guid UserId => actor.UserId ?? throw new UnauthorizedAccessException();
    public async Task<NotificationSettingsView> GetAsync(CancellationToken ct)
    {
        var row = await db.CustomerNotificationSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == UserId, ct);
        var verified = await db.Users.AnyAsync(u => u.Id == UserId && u.EmailVerified, ct);
        var linked = await db.TelegramBindings.AnyAsync(b => b.UserId == UserId, ct);
        return new(row?.OrdersEmail ?? false, row?.OrdersTelegram ?? false, row?.SupportEmail ?? false,
            row?.SupportTelegram ?? false, row?.Version ?? Guid.Empty, channels.EmailAvailable && verified, channels.TelegramAvailable && linked);
    }

    public async Task<NotificationSettingsView> SaveAsync(SaveNotificationSettingsRequest input, CancellationToken ct)
    {
        var row = await db.CustomerNotificationSettings.SingleOrDefaultAsync(x => x.UserId == UserId, ct);
        if ((row?.Version ?? Guid.Empty) != input.Version)
            throw new Application.Common.BusinessRuleException("Налаштування змінилися. Оновіть сторінку.");
        if (row is null) { row = new() { UserId = UserId }; db.CustomerNotificationSettings.Add(row); }
        row.OrdersEmail = input.OrdersEmail; row.OrdersTelegram = input.OrdersTelegram;
        row.SupportEmail = input.SupportEmail; row.SupportTelegram = input.SupportTelegram;
        row.Version = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }
}

// Stage with the business change; never send an external message inside the HTTP transaction.
public sealed class CustomerNotificationQueue(IApplicationDbContext db)
{
    public async Task OrderAsync(Order order, Guid eventId, string status, CancellationToken ct)
    {
        var label = OrderTitle(status);
        var shipment = db.OrderShipments.Local.FirstOrDefault(s => s.OrderId == order.Id)
            ?? await db.OrderShipments.AsNoTracking().SingleOrDefaultAsync(s => s.OrderId == order.Id, ct);
        var text = OrderText(order.OrderNumber, status);
        if (shipment?.Reference is { } reference)
            text += $" {(shipment.Carrier == "NovaPoshta" ? "Нова пошта" : "Кур’єр TREBA")}. Номер відправлення: {reference}.";
        await StageAsync(order.CustomerId, "Order", order.Id, eventId, "order:" + eventId, label, text,
            "/account/orders/" + order.Id, ct);
    }

    public static string OrderTitle(string status) => status switch
        {
            "AWAITING_PAYMENT" => "Замовлення прийнято", "PAID" => "Оплату замовлення підтверджено",
            "CONFIRMED" => "Замовлення підтверджено", "PROCESSING" => "Замовлення комплектується",
            "READY_FOR_SHIPMENT" => "Замовлення готове до відправлення", "SHIPPED" => "Статус замовлення: відправлено",
            "DELIVERED" => "Замовлення отримано", "COMPLETED" => "Замовлення виконано",
            "CANCELLED" => "Замовлення скасовано", "RETURN_REQUESTED" => "Повернення погоджено",
            "RETURNED" => "Товари прийнято після повернення", _ => "Статус замовлення оновлено"
        };

    public static string OrderText(string orderNumber, string status) => $"{OrderTitle(status)}. № {orderNumber}."
        + (status == "DELIVERED" ? " Усі товари доставлено. За бажанням залиште відгук про покупку в деталях замовлення." : "");

    public Task SupportAsync(SupportTicket ticket, SupportMessage message, CancellationToken ct) =>
        StageAsync(ticket.CustomerId, "Support", ticket.Id, message.Id, "support:" + message.Id,
            "Нова відповідь у TREBA", "Ви отримали відповідь у переписці. Увійдіть у свій акаунт TREBA, щоб її прочитати.",
            "/account/messages?ticket=" + ticket.Id, ct);

    private async Task StageAsync(Guid userId, string kind, Guid resourceId, Guid sourceId, string key,
        string title, string text, string path, CancellationToken ct)
    {
        var settings = await db.CustomerNotificationSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings is null) return;
        var email = kind == "Order" ? settings.OrdersEmail : settings.SupportEmail;
        var telegram = kind == "Order" ? settings.OrdersTelegram : settings.SupportTelegram;
        foreach (var channel in new[] { email ? "Email" : null, telegram ? "Telegram" : null }.OfType<string>())
            db.CustomerNotifications.Add(new() { UserId = userId, EventKey = key, Channel = channel, Kind = kind,
                ResourceId = resourceId, SourceId = sourceId, Title = title, Text = text, Path = path });
    }
}
