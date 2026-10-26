using Application.Abstractions;
using Application.Marketplace;
using Domain.Entities;
using Domain.Entities.Users;
using Infrastructure.Authentication;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApi.Telegram;

namespace WebApi.Notifications;

public sealed class CustomerNotificationOptions
{
    public bool Enabled { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public bool TelegramEnabled { get; set; } = true;
    public string StoreOrigin { get; set; } = "";
}

public sealed class NotificationChannels(IOptions<CustomerNotificationOptions> notifications,
    IOptions<SmtpEmailOptions> email, IOptions<TelegramOptions> telegram) : INotificationChannels
{
    public bool EmailAvailable => notifications.Value.Enabled && notifications.Value.EmailEnabled && email.Value.Enabled
        && !string.IsNullOrWhiteSpace(email.Value.Host) && !string.IsNullOrWhiteSpace(email.Value.FromAddress);
    public bool TelegramAvailable => notifications.Value.Enabled && notifications.Value.TelegramEnabled && telegram.Value.Configured;
}

public sealed class CustomerNotificationWorker(IServiceScopeFactory scopes, IOptions<CustomerNotificationOptions> options,
    ILogger<CustomerNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using (var scope = scopes.CreateScope())
            if (!scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.IsNpgsql()) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<CustomerNotificationRunner>().DeliverOneAsync(stoppingToken))
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Customer notifications paused after {ErrorType}.", ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}

public sealed class CustomerNotificationRunner(ApplicationDbContext db, ITransactionalEmailSender email,
    ITelegramBotClient telegram, INotificationChannels channels, IOptions<CustomerNotificationOptions> options, TimeProvider clock)
{
    public async Task<bool> DeliverOneAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return false;
        var now = clock.GetUtcNow();
        var emailEnabled = channels.EmailAvailable;
        var telegramEnabled = channels.TelegramAvailable;
        var item = await db.CustomerNotifications.Where(n => n.CompletedAt == null && n.AvailableAt <= now
                && (n.LeaseUntil == null || n.LeaseUntil < now)
                && (n.Channel == "Email" && emailEnabled || n.Channel == "Telegram" && telegramEnabled))
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).FirstOrDefaultAsync(ct);
        if (item is null) return false;
        item.LeaseUntil = now.AddMinutes(2); item.Attempts++; item.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return false; }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == item.UserId, ct);
        var preferences = await db.CustomerNotificationSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == item.UserId, ct);
        var binding = item.Channel == "Telegram" ? await db.TelegramBindings.AsNoTracking().SingleOrDefaultAsync(b => b.UserId == item.UserId, ct) : null;
        var subscribed = preferences is not null && (item.Kind, item.Channel) switch
        {
            ("Order", "Email") => preferences.OrdersEmail, ("Order", "Telegram") => preferences.OrdersTelegram,
            ("Support", "Email") => preferences.SupportEmail, ("Support", "Telegram") => preferences.SupportTelegram, _ => false
        };
        var visible = item.Kind switch
        {
            "Order" => await db.Orders.AsNoTracking().AnyAsync(o => o.Id == item.ResourceId && o.CustomerId == item.UserId, ct),
            "Support" => await db.SupportTickets.AsNoTracking().AnyAsync(t => t.Id == item.ResourceId && t.CustomerId == item.UserId
                && db.SupportMessages.Any(m => m.Id == item.SourceId && m.TicketId == t.Id && !m.Internal && m.AuthorId != item.UserId), ct),
            _ => false
        };
        if (item.CreatedAt < now.AddDays(-7) || item.Attempts > 5 || user is null || user.IsDeleted || user.Status != UserStatus.Active
            || !subscribed || !visible || item.Channel == "Email" && !user.EmailVerified || item.Channel == "Telegram" && binding is null)
            Complete(item, "Skipped: expired, unsubscribed or unavailable");
        else
        {
            try
            {
                var url = options.Value.StoreOrigin.TrimEnd('/') + item.Path;
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                sendTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                if (item.Channel == "Email")
                    await email.SendNotificationAsync(user.Email, item.Title, item.Text, url, sendTimeout.Token);
                else
                {
                    using var response = await telegram.CallAsync("sendMessage", new { chat_id = binding!.ChatId,
                        text = item.Title + "\n\n" + item.Text,
                        reply_markup = new { inline_keyboard = new[] { new[] { new { text = "Відкрити TREBA", url } } } } }, sendTimeout.Token);
                }
                Complete(item, "Sent");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (item.Attempts >= 5 || ex is TelegramBotException { HttpStatus: 400 or 403 }) Complete(item, "Delivery unavailable");
                else { item.AvailableAt = clock.GetUtcNow().AddSeconds(Math.Min(1800, 30 * Math.Pow(2, item.Attempts))); item.Result = "Waiting to retry"; }
            }
        }
        item.LeaseUntil = null; item.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { }
        return true;
    }
    private void Complete(CustomerNotification item, string result) { item.CompletedAt = clock.GetUtcNow(); item.Result = result; }
}
