using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace AdminPanel.Auth;

public class JwtAuthenticationStateProvider(TokenStorageService storage) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    public override async Task<AuthenticationState> GetAuthenticationStateAsync() => FromToken(await storage.GetTokenAsync());
    public async Task NotifyUserAuthenticationAsync(string token)
    {
        await storage.SetTokenAsync(token);
        NotifyAuthenticationStateChanged(Task.FromResult(FromToken(token)));
    }
    public async Task NotifyUserLogoutAsync()
    {
        await storage.ClearTokensAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }
    // UI hints only; every API call independently validates the JWT signature and current user.
    private static AuthenticationState FromToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return Anonymous;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return Anonymous;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!doc.RootElement.TryGetProperty("exp", out var exp) || exp.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return Anonymous;
            var claims = new List<Claim>();
            foreach (var item in doc.RootElement.EnumerateObject())
            {
                var key = item.Name switch { "role" => ClaimTypes.Role, "sub" => ClaimTypes.NameIdentifier, "email" => ClaimTypes.Email, _ => item.Name };
                if (item.Value.ValueKind == JsonValueKind.Array)
                    claims.AddRange(item.Value.EnumerateArray().Select(value => new Claim(key, value.ToString())));
                else claims.Add(new Claim(key, item.Value.ToString()));
            }
            return new(new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt")));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or OverflowException) { return Anonymous; }
    }
}
