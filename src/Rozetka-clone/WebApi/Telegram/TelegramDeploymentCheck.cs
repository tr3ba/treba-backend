using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace WebApi.Telegram;

// An operator-only CLI command. Does not start HTTP, poll updates, send messages or change the database.
public static class TelegramDeploymentCheck
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await using var scope = services.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;
            if (!options.Configured || !options.LongPollingEnabled)
                throw new InvalidOperationException("Telegram server settings are incomplete or long polling is disabled.");
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var applied = (await db.Database.GetAppliedMigrationsAsync(timeout.Token)).ToHashSet(StringComparer.Ordinal);
            string[] required = ["20261017110000_AddAdminAuditLog", "20261018112728_AddTelegramWorkspace"];
            var missing = required.Where(migration => !applied.Contains(migration)).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException("Required database migrations are missing: " + string.Join(", ", missing));
            Console.WriteLine("Telegram database migrations: present.");

            var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
            using var me = await bot.CallAsync("getMe", new { }, timeout.Token);
            var username = me.RootElement.GetProperty("result").GetProperty("username").GetString();
            if (!string.Equals(username, options.BotUsername, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The server token belongs to a different Telegram bot.");
            Console.WriteLine("Telegram token: valid for @" + options.BotUsername + ".");
            using var webhook = await bot.CallAsync("getWebhookInfo", new { }, timeout.Token);
            if (!string.IsNullOrEmpty(webhook.RootElement.GetProperty("result").GetProperty("url").GetString()))
                throw new InvalidOperationException("The bot has an active webhook; long polling requires removing it first.");
            Console.WriteLine("Telegram webhook: absent. Deployment preflight passed.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine("Telegram preflight failed: " + exception.Message);
        }
        catch (TelegramBotException exception)
        {
            Console.Error.WriteLine("Telegram preflight failed: " + exception.Message);
        }
        catch (Exception exception)
        {
            // Connection strings and HTTP request URLs must never reach deployment output.
            Console.Error.WriteLine("Telegram preflight failed (" + exception.GetType().Name + "). Check server connectivity and private logs.");
        }
        return 1;
    }
}
