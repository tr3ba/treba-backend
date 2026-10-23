using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace WebApi.Telegram;

public sealed record VerifiedTelegramUser(long Id, string? Username, string Name, DateTimeOffset AuthenticatedAt);

public sealed class TelegramInitDataValidator(TimeProvider clock)
{
    public VerifiedTelegramUser Validate(string initData, string botToken, TimeSpan maxAge)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(initData) || initData.Length > 16384)
            throw new UnauthorizedAccessException("Відкрийте застосунок із Telegram.");
        var fields = QueryHelpers.ParseQuery(initData);
        if (fields.Any(f => f.Value.Count != 1) || !fields.TryGetValue("hash", out var hash) || hash.ToString().Length != 64)
            throw new UnauthorizedAccessException("Недійсний підпис Telegram.");
        var check = string.Join('\n', fields.Where(f => f.Key != "hash").OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + f.Value));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check));
        byte[] supplied;
        try { supplied = Convert.FromHexString(hash.ToString()); }
        catch (FormatException) { throw new UnauthorizedAccessException("Недійсний підпис Telegram."); }
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied)
            || !fields.TryGetValue("auth_date", out var date) || !long.TryParse(date, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
            throw new UnauthorizedAccessException("Недійсний підпис Telegram.");
        var now = clock.GetUtcNow();
        if (timestamp < now.Subtract(maxAge).ToUnixTimeSeconds() || timestamp > now.AddSeconds(30).ToUnixTimeSeconds())
            throw new UnauthorizedAccessException("Термін дії сеансу Telegram минув. Відкрийте застосунок знову.");
        try
        {
            using var json = JsonDocument.Parse(fields["user"].ToString());
            var user = json.RootElement;
            var id = user.GetProperty("id").GetInt64();
            if (id <= 0 || user.TryGetProperty("is_bot", out var isBot) && isBot.ValueKind == JsonValueKind.True) throw new JsonException();
            var name = string.Join(' ', new[] { Get(user, "first_name"), Get(user, "last_name") }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return new(id, Get(user, "username"), name[..Math.Min(200, name.Length)], DateTimeOffset.FromUnixTimeSeconds(timestamp));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new UnauthorizedAccessException("Недійсні дані користувача Telegram."); }
    }
    private static string? Get(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
