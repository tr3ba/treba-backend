using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Abstractions;
using Application.Administration;
using Application.Reviews;
using Contracts.Admin.Dashboard;
using Contracts.Admin.Reviews;
using Domain.Entities;
using Domain.Entities.Product;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Administration;
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
using WebApi.Errors;

namespace WebApi.Tests;

public sealed class AdminHttpTests
{
    internal static async Task<WebApplication> ServerAsync(Action<IServiceCollection>? configure = null)
    {
        var database = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var services = builder.Services;
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<AdminWorkspaceService>();
        services.AddScoped<StaffSettingsService>();
        services.AddScoped<Application.Catalog.CatalogService>();
        services.AddScoped<Application.Products.IProductService, Application.Products.ProductService>();
        services.AddScoped<Application.Attributes.IAttributeService, Application.Attributes.AttributeService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, WebApi.Authentication.HttpCurrentActor>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<Application.Inventory.InventoryService>();
        services.AddScoped<Application.Inventory.IInventoryService>(sp =>
            sp.GetRequiredService<Application.Inventory.InventoryService>()
        );
        services.AddScoped<Application.Promotions.IDiscountService, Application.Promotions.DiscountService>();
        services.AddScoped<Application.Payments.IPaymentService, TestPaymentService>();
        Application.Orders.OrderRegistration.AddOrderServices(services);
        services.AddScoped<PromotionManagementService>();
        services.AddScoped<SupportService>();
        services.AddScoped<FeatureSettingsService>();
        services.AddScoped<ReviewReplyTemplateService>();
        services.AddScoped<OperationsService>();
        services.AddScoped<Application.Marketplace.SellerApplicationService>();
        services.AddScoped<Application.Marketplace.CustomerAccountService>();
        services.AddScoped<Application.Marketplace.NotificationService>();
        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
        services.AddAuthorization();
        services.AddControllers().AddApplicationPart(typeof(AdminReviewsController).Assembly);
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        configure?.Invoke(services);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseAuthentication();
        if (configure is not null) app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task ModerationHttpRequest_ValidatesBody_AndPersistsRating()
    {
        await using var server = await ServerAsync();
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        var id = Guid.NewGuid();
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Product",
                Slug = "product",
            };
            db.Products.Add(product);
            db.Reviews.Add(
                new Review
                {
                    Id = id,
                    ProductId = product.Id,
                    Rating = 5,
                    Status = ReviewStatus.PENDING,
                }
            );
            await db.SaveChangesAsync();
        }

        using var invalid = await client.PutAsJsonAsync($"/api/v1/admin/reviews/{id}/moderate", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var published = await client.PutAsJsonAsync(
            $"/api/v1/admin/reviews/{id}/moderate",
            new ModerateReviewRequest("PUBLISHED")
        );
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(ReviewStatus.PUBLISHED, (await db.Reviews.SingleAsync()).Status);
            var product = await db.Products.SingleAsync();
            Assert.Equal(1, product.ReviewCount);
            Assert.Equal(5, product.AverageRating);
        }
        using var missing = await client.PutAsJsonAsync(
            $"/api/v1/admin/reviews/{Guid.NewGuid()}/moderate",
            new ModerateReviewRequest("REJECTED")
        );
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalidState = await client.PutAsJsonAsync(
            $"/api/v1/admin/reviews/{id}/moderate",
            new ModerateReviewRequest("PENDING")
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalidState.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Seller", HttpStatusCode.Forbidden)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Moderator", HttpStatusCode.OK)]
    [InlineData("Manager", HttpStatusCode.OK)]
    [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task AdministrativeRoutes_EnforceRolePolicies(string? role, HttpStatusCode expected)
    {
        await using var server = await ServerAsync();
        using var client = server.GetTestClient();
        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var list = await client.GetAsync("/api/v1/admin/reviews");
        using var dashboard = await client.GetAsync("/api/v1/admin/dashboard");
        Assert.Equal(expected, list.StatusCode);
        Assert.Equal(expected, dashboard.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var data = await dashboard.Content.ReadFromJsonAsync<AdminDashboardDto>();
            Assert.NotNull(data);
            Assert.Equal(role == "Administrator", data.TotalUsers.HasValue);
            Assert.Equal(role is "Administrator" or "Manager", data.PendingSellers.HasValue);
        }
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Seller", HttpStatusCode.Forbidden)]
    [InlineData("Manager", HttpStatusCode.Forbidden)]
    [InlineData("Moderator", HttpStatusCode.Forbidden)]
    [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task SettingsRoutes_AreAdministratorOnly(string? role, HttpStatusCode expected)
    {
        await using var server = await ServerAsync();
        using var client = server.GetTestClient();
        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var staff = await client.GetAsync("/api/v1/admin/settings/staff");
        using var audit = await client.GetAsync("/api/v1/admin/settings/audit");
        Assert.Equal(expected, staff.StatusCode);
        Assert.Equal(expected, audit.StatusCode);
    }

    private sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.Role, role.ToString()),
                new("firstName", "Test"),
                new("lastName", "Staff"),
            };
            if (Request.Headers.TryGetValue("X-Test-UserId", out var user))
                claims.Add(new(ClaimTypes.NameIdentifier, user.ToString()));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }
}
