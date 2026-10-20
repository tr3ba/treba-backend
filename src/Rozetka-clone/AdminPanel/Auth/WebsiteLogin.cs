using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Contracts.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace AdminPanel.Auth;

public static class WebsiteLogin
{
    public const string Scheme = "Website";
    public const string AccessClaim = "treba_site_access";
    public const string RefreshClaim = "treba_site_refresh";
    private const string StateCookie = "treba_admin_state";

    public static void Map(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        if (!app.Configuration.GetValue<bool>("WebsiteLogin:Enabled")) return;
        var store = Origin(app.Configuration["WebsiteLogin:StoreOrigin"], app.Environment.IsDevelopment());
        var admin = Origin(app.Configuration["WebsiteLogin:AdminOrigin"], app.Environment.IsDevelopment());
        var stateOptions = new CookieOptions { HttpOnly = true, Secure = !app.Environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/site-login", MaxAge = TimeSpan.FromMinutes(5) };

        app.MapGet("/site-login/start", (HttpContext context) =>
        {
            Private(context);
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            context.Response.Cookies.Append(StateCookie, state, stateOptions);
            return Results.Redirect(store + "/shop-api/admin-entry?state=" + state);
        });
        app.MapGet("/site-login/callback", async (HttpContext context, IHttpClientFactory clients, CancellationToken ct) =>
        {
            Private(context);
            var state = context.Request.Query["state"].ToString();
            var expected = context.Request.Cookies[StateCookie];
            if (state.Length != 64 || expected is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expected))) return Results.BadRequest("Начните вход с сайта заново.");
            context.Response.Cookies.Delete(StateCookie, stateOptions);
            var code = context.Request.Query["code"].ToString();
            if (code.Length > 100) return Results.BadRequest();
            try
            {
                using var response = await clients.CreateClient("website-login").PostAsJsonAsync("api/auth/admin-entry/redeem", new RedeemAdminEntryRequest(code), ct);
                if (!response.IsSuccessStatusCode) return Results.Redirect(admin + "/?login=expired");
                var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(ct);
                if (auth is null || !AdminAccess.IsStaff(auth.Role)) return Results.StatusCode(403);
                var principal = new ClaimsPrincipal(new ClaimsIdentity([
                    new(ClaimTypes.NameIdentifier, auth.UserId.ToString()), new(ClaimTypes.Role, auth.Role),
                    new(AccessClaim, auth.AccessToken), new(RefreshClaim, auth.RefreshToken)], Scheme));
                await context.SignInAsync(Scheme, principal, new AuthenticationProperties { ExpiresUtc = auth.ExpiresAt, IsPersistent = false, AllowRefresh = false });
                return Results.Redirect(admin + "/");
            }
            catch (HttpRequestException) { return Results.StatusCode(503); }
        });
        app.MapPost("/site-login/logout", async (HttpContext context) =>
        {
            Private(context);
            if (!Uri.TryCreate(context.Request.Headers.Origin, UriKind.Absolute, out var origin) || origin.GetLeftPart(UriPartial.Authority) != admin) return Results.BadRequest();
            await context.SignOutAsync(Scheme); return Results.NoContent();
        });
    }

    private static string Origin(string? value, bool development)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != ""
            || uri.Scheme != "https" && !(development && uri.IsLoopback && uri.Scheme == "http")) throw new InvalidOperationException("Configure an HTTPS origin for website login.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    private static void Private(HttpContext context)
    { context.Response.Headers.CacheControl = "no-store, private"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; }
}
