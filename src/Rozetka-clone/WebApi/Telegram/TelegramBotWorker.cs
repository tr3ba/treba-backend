using System.Data;
using System.Text.Json;
using Application.Common;
using Domain.Entities.Telegram;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace WebApi.Telegram;

public sealed class TelegramBotWorker(IServiceScopeFactory scopes, IOptions<TelegramOptions> options, ITelegramBotClient bot,
    ILogger<TelegramBotWorker> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Configured || !options.Value.LongPollingEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (TelegramBotException ex) { logger.LogWarning("{BotError}", ex.Message); }
            catch (Exception ex) { logger.LogWarning("Telegram worker paused after {ErrorType}. Check database migrations and bot configuration.", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
    private async Task PollAsync(CancellationToken ct)
    {
        using var meResponse = await bot.CallAsync("getMe", new { }, ct);
        var me = meResponse.RootElement.GetProperty("result");
        if (!string.Equals(me.GetProperty("username").GetString(), options.Value.BotUsername, StringComparison.OrdinalIgnoreCase))
            throw new TelegramBotException("Telegram bot username does not match the configured token.");
        var botId = me.GetProperty("id").GetInt64();
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var postgres = db.Database.IsNpgsql(); var leased = false; var lockKey = 0x7452454200000000L ^ botId;
        try
        {
            if (postgres)
            {
                await db.Database.OpenConnectionAsync(ct);
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT pg_try_advisory_lock(@key)";
                var key = command.CreateParameter(); key.ParameterName = "key"; key.DbType = DbType.Int64; key.Value = lockKey; command.Parameters.Add(key);
                leased = (bool)(await command.ExecuteScalarAsync(ct))!;
                if (!leased) return; // Only one API instance may consume updates for this bot.
            }
            var cursor = await db.TelegramBotCursors.SingleOrDefaultAsync(c => c.BotId == botId, ct);
            if (cursor is null) { cursor = new() { BotId = botId }; db.TelegramBotCursors.Add(cursor); await db.SaveChangesAsync(ct); }
            using (await bot.CallAsync("setChatMenuButton", new { menu_button = new { type = "web_app", text = "Treba", web_app = new { url = options.Value.MiniAppUrl } } }, ct)) { }
            var cleanupAt = clock.GetUtcNow();
            while (!ct.IsCancellationRequested)
            {
                using var response = await bot.CallAsync("getUpdates", new { offset = cursor.LastUpdateId + 1, timeout = 25, limit = 20, allowed_updates = new[] { "message" } }, ct);
                foreach (var update in response.RootElement.GetProperty("result").EnumerateArray())
                {
                    var updateId = update.GetProperty("update_id").GetInt64();
                    if (updateId <= cursor.LastUpdateId) continue;
                    await HandleUpdateAsync(update, ct);
                    cursor.LastUpdateId = updateId; await db.SaveChangesAsync(ct);
                }
                if (clock.GetUtcNow() >= cleanupAt)
                {
                    var before = clock.GetUtcNow().AddDays(-7);
                    await db.TelegramGuardChallenges.Where(c => c.ExpiresAt < before).ExecuteDeleteAsync(ct);
                    await db.TelegramLinkChallenges.Where(c => c.ExpiresAt < before).ExecuteDeleteAsync(ct);
                    await db.ProductImportDrafts.Where(d => d.SubmittedAt == null && d.ExpiresAt < before).ExecuteDeleteAsync(ct);
                    cleanupAt = clock.GetUtcNow().AddHours(1);
                }
            }
        }
        finally
        {
            if (postgres && leased)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT pg_advisory_unlock(@key)";
                var key = command.CreateParameter(); key.ParameterName = "key"; key.DbType = DbType.Int64; key.Value = lockKey; command.Parameters.Add(key);
                try { await command.ExecuteScalarAsync(CancellationToken.None); } catch (Exception) { }
            }
        }
    }
    public async Task HandleUpdateAsync(JsonElement update, CancellationToken ct)
    {
        if (!update.TryGetProperty("message", out var message) || !message.TryGetProperty("text", out var textNode)
            || !message.TryGetProperty("from", out var from) || !message.TryGetProperty("chat", out var chat)
            || chat.GetProperty("type").GetString() != "private" || from.TryGetProperty("is_bot", out var isBot) && isBot.GetBoolean()) return;
        var telegramId = from.GetProperty("id").GetInt64(); var chatId = chat.GetProperty("id").GetInt64();
        if (telegramId != chatId) return;
        var text = textNode.GetString() ?? "";
        if (text.Length > 200 || !(text.StartsWith("/start", StringComparison.Ordinal) || text is "/menu" or "/guard")) return;
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var links = scope.ServiceProvider.GetRequiredService<TelegramLinkService>();
        try
        {
            var binding = await db.TelegramBindings.AsNoTracking().SingleOrDefaultAsync(b => b.TelegramUserId == telegramId, ct);
            if (binding is not null)
            {
                var user = await links.RequireUserAsync(binding.UserId, ct, allowCustomer: true);
                if (user.Role!.Name == "Customer")
                {
                    await bot.SendTextAsync(chatId, "Telegram підключено до вашого акаунта TREBA. Керуйте сповіщеннями про замовлення та відповіді підтримки у розділі «Підписки» на сайті.", ct);
                    return;
                }
                await bot.SendMenuAsync(chatId, $"Вітаємо в Treba, {user.FirstName} {user.LastName}!\nРоль: {(user.Role!.Name == "Seller" ? "Продавець" : "Модератор")}\nID Treba: {user.Id}\nID Telegram: {telegramId}\n\nВідкрийте робоче меню або підтвердьте вхід через Guard.", ct);
                return;
            }
            if (text.StartsWith("/start link_", StringComparison.Ordinal))
            {
                var name = string.Join(' ', new[] { String(from, "first_name"), String(from, "last_name") }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var user = await links.ClaimAsync(text[12..].Trim(), telegramId, chatId, String(from, "username"), name, ct);
                if (user.Role == "Customer")
                {
                    await bot.SendTextAsync(chatId, "Поверніться на сайт TREBA та натисніть «Підтвердити підключення». Потім увімкніть потрібні сповіщення у розділі «Підписки».", ct);
                    return;
                }
                await bot.SendTextAsync(chatId, $"Вітаємо в Treba, {user.Name}!\nРоль: {(user.Role == "Seller" ? "Продавець" : "Модератор")}\nID Treba: {user.Id}\nID Telegram: {telegramId}\n\nПоверніться до профілю на сайті та натисніть «Підтвердити підключення». Потім надішліть /menu — відкриється робоча панель.", ct);
            }
            else await bot.SendTextAsync(chatId, "Вітаємо! Це робочий бот Treba для продавців і модераторів.\nЩоб почати, відкрийте свій профіль на сайті Treba та натисніть «Підключити Telegram».", ct);
        }
        catch (Exception ex) when (ex is BusinessRuleException or UnauthorizedAccessException or ArgumentException)
        { await bot.SendTextAsync(chatId, ex.Message, ct); }
        catch (DbUpdateConcurrencyException) { await bot.SendTextAsync(chatId, "Посилання вже оброблено. Перевірте підключення в профілі Treba.", ct); }
    }
    private static string? String(JsonElement node, string key) => node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
