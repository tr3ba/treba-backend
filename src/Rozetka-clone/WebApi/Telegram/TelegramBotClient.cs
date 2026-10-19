using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WebApi.Telegram;

public interface ITelegramBotClient
{
    Task<JsonDocument> CallAsync(string method, object payload, CancellationToken ct);
    Task SendMenuAsync(long chatId, string text, CancellationToken ct);
    Task SendTextAsync(long chatId, string text, CancellationToken ct);
}

// Deliberately not an IHttpClientFactory client: its request logging would include the bot token in URLs.
public sealed class TelegramBotClient(IOptions<TelegramOptions> options) : ITelegramBotClient, IDisposable
{
    private readonly HttpClient client = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) }) { Timeout = TimeSpan.FromSeconds(40) };
    public async Task<JsonDocument> CallAsync(string method, object payload, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("https://api.telegram.org/bot" + options.Value.BotToken + "/" + method, payload, ct);
            if (!response.IsSuccessStatusCode) throw new TelegramBotException("Telegram Bot API returned HTTP " + (int)response.StatusCode + ".");
            var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) { document.Dispose(); throw new TelegramBotException("Telegram Bot API rejected the request."); }
            return document;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        { throw new TelegramBotException("Telegram Bot API is temporarily unavailable."); }
    }
    public async Task SendMenuAsync(long chatId, string text, CancellationToken ct)
    {
        var url = options.Value.MiniAppUrl;
        var guardUrl = url + (url.Contains('?') ? "&" : "?") + "view=guard";
        using var response = await CallAsync("sendMessage", new { chat_id = chatId, text, reply_markup = new { inline_keyboard = new[] {
            new[] { new { text = "Открыть Treba", web_app = new { url } } },
            new[] { new { text = "Вход через Guard", web_app = new { url = guardUrl } } } } } }, ct);
    }
    public async Task SendTextAsync(long chatId, string text, CancellationToken ct)
    { using var response = await CallAsync("sendMessage", new { chat_id = chatId, text }, ct); }
    public void Dispose() => client.Dispose();
}

public sealed class TelegramBotException(string message) : Exception(message);
