using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AdminPanel.Auth;
using Contracts.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace WebApi.Tests;

public sealed class WebsiteAdminSessionTests
{
    private sealed class Api : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Assert.Equal("/api/auth/admin-entry/redeem", request.RequestUri!.AbsolutePath);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(
                        new AuthResponse(
                            Guid.NewGuid(),
                            "fixture@example.test",
                            "Test",
                            "Manager",
                            "Manager",
                            "server-only-access",
                            "server-only-refresh",
                            DateTimeOffset.UtcNow.AddMinutes(30)
                        )
                    ),
                }
            );
        }
    }

    private static async Task<WebApplication> Server(Api api, bool enabled)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["WebsiteLogin:Enabled"] = enabled.ToString(),
                ["WebsiteLogin:StoreOrigin"] = "https://shop.fixture",
                ["WebsiteLogin:AdminOrigin"] = "https://admin.fixture",
            }
        );
        builder.Services.AddAuthentication(WebsiteLogin.Scheme).AddCookie(WebsiteLogin.Scheme);
        builder
            .Services.AddHttpClient("website-login", c => c.BaseAddress = new("https://api.fixture/"))
            .ConfigurePrimaryHttpMessageHandler(() => api);
        var app = builder.Build();
        app.UseAuthentication();
        WebsiteLogin.Map(app);
        app.MapGet(
            "/whoami",
            (Microsoft.AspNetCore.Http.HttpContext context) =>
                new
                {
                    authenticated = context.User.Identity?.IsAuthenticated,
                    role = context.User.FindFirst(ClaimTypes.Role)?.Value,
                }
        );
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task WebsiteEntry_RequiresBrowserBoundState_AndStoresSessionInHttpOnlyCookie()
    {
        var api = new Api();
        await using var app = await Server(api, true);
        using var client = app.GetTestClient();
        client.BaseAddress = new("https://admin.fixture");
        using var started = await client.GetAsync("/site-login/start");
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        var cookie = started.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        var location = started.Headers.Location!;
        Assert.Equal("shop.fixture", location.Host);
        var state = System.Web.HttpUtility.ParseQueryString(location.Query)["state"]!;
        Assert.Equal(64, state.Length);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/site-login/callback?state=" + state + "&code=one-time")).StatusCode
        );
        Assert.Equal(0, api.Requests);
        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        using var accepted = await client.GetAsync("/site-login/callback?state=" + state + "&code=one-time");
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal("https://admin.fixture/", accepted.Headers.Location!.ToString());
        Assert.Equal(1, api.Requests);
        Assert.DoesNotContain("server-only", accepted.Headers.Location.ToString());
        Assert.True(accepted.Headers.CacheControl!.NoStore);
        var session = accepted.Headers.GetValues("Set-Cookie").Single(v => v.Contains(".AspNetCore.Website="));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server-only", session);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", session.Split(';')[0]);
        var identity = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/whoami");
        Assert.True(identity!["authenticated"]!.GetValue<bool>());
        Assert.Equal("Manager", identity["role"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/site-login/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "https://admin.fixture");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/site-login/logout", null)).StatusCode);
    }

    [Fact]
    public async Task DevelopmentByDefault_KeepsItsIndependentLogin()
    {
        await using var app = await Server(new(), false);
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/site-login/start")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Theory]
    [InlineData("/orders/fixture?tab=items", "/orders/fixture?tab=items")]
    [InlineData("/users", "/")]
    [InlineData("//outside.test", "/")]
    public async Task WebsiteEntry_PreservesAnAuthorizedNotificationDestination(string destination, string expected)
    {
        var api = new Api();
        await using var app = await Server(api, true);
        using var client = app.GetTestClient();
        client.BaseAddress = new("https://admin.fixture");
        using var start = await client.GetAsync("/site-login/start?returnUrl=" + Uri.EscapeDataString(destination));
        var state = System.Web.HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"]!;
        client.DefaultRequestHeaders.Add(
            "Cookie",
            string.Join("; ", start.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]))
        );
        using var complete = await client.GetAsync("/site-login/callback?state=" + state + "&code=one-time");
        Assert.Equal("https://admin.fixture" + expected, complete.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("https://outside.test")]
    [InlineData("//outside.test")]
    [InlineData("/\\outside.test")]
    [InlineData("/%2f%2foutside.test")]
    [InlineData("/site-login/start")]
    [InlineData("/orders\n")]
    public void NotificationReturn_RejectsExternalOrRecursivePaths(string value) =>
        Assert.Equal("/", WebsiteLogin.SafeReturnPath(value));

    private sealed class Storage : IJSRuntime
    {
        public int Reads;

        public ValueTask<TValue> InvokeAsync<TValue>(string name, object?[]? args)
        {
            Reads++;
            return ValueTask.FromResult((TValue)(object)"independent-dev-token");
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string name, CancellationToken ct, object?[]? args) =>
            InvokeAsync<TValue>(name, args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebsiteCookie_DoesNotReplaceTheIndependentDevelopmentSession(bool enabled)
    {
        var browser = new Storage();
        var context = new Microsoft.AspNetCore.Http.HttpContextAccessor
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        };
        context.HttpContext.User = new(
            new ClaimsIdentity([new(WebsiteLogin.AccessClaim, "website-cookie-token")], "Website")
        );
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WebsiteLogin:Enabled"] = enabled.ToString() })
            .Build();
        var storage = new TokenStorageService(browser, context, config);
        Assert.Equal(enabled ? "website-cookie-token" : "independent-dev-token", await storage.GetTokenAsync());
        Assert.Equal(enabled ? 0 : 1, browser.Reads);
    }
}
