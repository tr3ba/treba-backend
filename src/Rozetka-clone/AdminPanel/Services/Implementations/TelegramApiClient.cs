using System.Net.Http.Json;
using System.Text.Json;
using Contracts.Telegram;

namespace AdminPanel.Services.Implementations;

public sealed class TelegramApiClient(HttpClient http)
{
    public async Task<T> Get<T>(string path, CancellationToken ct = default)
    { using var response = await http.GetAsync(path, ct); await Check(response); return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!; }
    public async Task<T> Post<T>(string path, object body, CancellationToken ct = default)
    { using var response = await http.PostAsJsonAsync(path, body, ct); await Check(response); return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!; }
    public async Task Put(string path, object body)
    { using var response = await http.PutAsJsonAsync(path, body); await Check(response); }
    public async Task Unlink()
    { using var response = await http.DeleteAsync("api/telegram/link"); await Check(response); }
    private static async Task Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = "Telegram request failed. Please try again.";
        try { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.TryGetProperty("title", out var title) || json.RootElement.TryGetProperty("message", out title)) message = title.GetString() ?? message; }
        catch (JsonException) { }
        throw new HttpRequestException(message, null, response.StatusCode);
    }
}
