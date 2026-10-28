using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Abstractions;
using Application.Carts;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebApi.Controllers;

namespace WebApi.Tests;

public sealed class CustomerRoleBoundaryTests
{
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Fixture-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(ClaimTypes.Role, role), new(ClaimTypes.NameIdentifier, "bbf0b073-0533-44bf-9f72-f67fd7bb0d3b")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    [Fact]
    public async Task StaffCannotUseCustomerEndpoints_WhileCustomerCartRemainsAvailable()
    {
        await using var db = TelegramTestData.Memory();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IApplicationDbContext>(db);
        builder.Services.AddScoped<ICartService, CartService>();
        builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, Authentication>("Fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(CartController).Assembly);
        await using var app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        const string id = "bbf0b073-0533-44bf-9f72-f67fd7bb0d3b";
        (HttpMethod method, string path)[] endpoints = [
            (HttpMethod.Get, "/api/v1/cart"), (HttpMethod.Post, "/api/v1/cart/items"),
            (HttpMethod.Get, "/api/v1/wishlist"), (HttpMethod.Post, "/api/v1/orders"),
            (HttpMethod.Get, "/api/v1/orders/" + id), (HttpMethod.Post, "/api/v1/orders/" + id + "/cancel"),
            (HttpMethod.Post, "/api/v1/payments/orders/" + id + "/payments"),
            (HttpMethod.Get, "/api/v1/checkout-delivery/capabilities"),
            (HttpMethod.Get, "/api/v1/account/preferences"), (HttpMethod.Patch, "/api/v1/account/preferences"),
            (HttpMethod.Get, "/api/v1/account/orders"), (HttpMethod.Get, "/api/v1/account/shipments"),
            (HttpMethod.Get, "/api/v1/account/seller-application"), (HttpMethod.Get, "/api/v1/account/notification-settings"),
            (HttpMethod.Post, "/api/v1/reviews"),
            (HttpMethod.Get, "/api/v1/reviews/awaiting"), (HttpMethod.Get, "/api/v1/reviews/mine")];
        foreach (var role in new[] { "Administrator", "Manager", "Moderator", "Seller", "Unknown" })
        {
            client.DefaultRequestHeaders.Remove("X-Fixture-Role"); client.DefaultRequestHeaders.Add("X-Fixture-Role", role);
            foreach (var (method, path) in endpoints)
            {
                using var request = new HttpRequestMessage(method, path);
                if (method != HttpMethod.Get) request.Content = JsonContent.Create(new { });
                using var response = await client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, role + " " + method + " " + path + " => " + response.StatusCode);
            }
        }
        Assert.Empty(await db.Carts.ToListAsync()); Assert.Empty(await db.Orders.ToListAsync());
        client.DefaultRequestHeaders.Remove("X-Fixture-Role");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/cart")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Fixture-Role", "Customer");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/cart")).StatusCode);
    }
}
