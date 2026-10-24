using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AdminPanel.Components;
using AdminPanel.Pages.Customers;
using AdminPanel.Pages.Loyalty;
using AdminPanel.Pages.Orders;
using AdminPanel.Pages.ProductsModeration;
using AdminPanel.Pages.Settings;
using AdminPanel.Pages.Support;
using AdminPanel.Services.Implementations;
using AdminPanel.State;
using Contracts.Admin.Operations;
using Contracts.Admin.Settings;
using Contracts.Catalog;
using Domain.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace WebApi.Tests;

public sealed class OperationsUiTests
{
    private static readonly Guid OrderId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        TicketId = Guid.NewGuid();
    private static readonly OrderRow Order = new(
        OrderId,
        "ORD-UI-FIXTURE",
        CustomerId,
        "Customer Fixture",
        "AWAITING_PAYMENT",
        250,
        "UAH",
        2,
        DateTimeOffset.UtcNow
    );
    private static readonly TicketRow Ticket = new(
        TicketId,
        OrderId,
        Order.OrderNumber,
        CustomerId,
        Order.CustomerName,
        "RETURN",
        "Return fixture",
        "APPROVED",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        Guid.NewGuid()
    );

    [Theory]
    [InlineData("uk", "Наступний статус", "Чернетка залишається неактивною")]
    [InlineData("en", "Next status", "Drafts stay inactive")]
    public async Task OperationalPages_RenderLocalizedDataAndControls(
        string culture,
        string nextStatus,
        string draftHint
    )
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var order = await Render<OrderDetailsPage>(new() { ["Id"] = OrderId });
            Assert.Contains("ORD-UI-FIXTURE", order);
            Assert.Contains(nextStatus, order);
            Assert.DoesNotContain("Order_AWAITING_PAYMENT", order);
            var promotion = await Render<PromotionEditor>([]);
            Assert.Contains(draftHint, promotion);
            Assert.DoesNotContain("DraftScheduleHint", promotion);
            var ticket = await Render<TicketDetailsPage>(new() { ["Id"] = TicketId });
            Assert.Contains("Return fixture", ticket);
            Assert.DoesNotContain("ConfirmReturnReceived", ticket);
            Assert.Contains("rows=\"4\"", ticket);
            var customer = await Render<CustomerDetailsPage>(new() { ["Id"] = CustomerId });
            Assert.Contains("Customer Fixture", customer);
            Assert.Contains("ORD-UI-FIXTURE", customer);
            Assert.DoesNotContain("RecentHundred", customer);
            var moderation = await Render<ModerationQueue>([]);
            Assert.Contains("Moderation fixture product", moderation);
            Assert.DoesNotContain("ResubmissionChanges", moderation);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("Seller", false)]
    [InlineData("Moderator", false)]
    [InlineData("Manager", true)]
    [InlineData("Administrator", true)]
    public async Task Metrics_RenderOnlyForOperationalStaff_AndLinkToTheRightQueues(string role, bool visible)
    {
        var html = await Render<BusinessOperationsDashboard>([], role);
        Assert.Equal(visible, html.Contains("/support?view=overdue"));
        if (visible)
        {
            Assert.Contains("/products?stock=out&status=ACTIVE", html);
            Assert.DoesNotContain("amp;amp;", html);
        }
        Assert.False(AdminPanel.Auth.AdminAccess.CanAccess(role, "/storefront"));
        Assert.Equal(
            role is "Seller" or "Manager" or "Administrator",
            AdminPanel.Auth.AdminAccess.CanAccess(role, "/orders/" + OrderId)
        );
        Assert.True(AdminPanel.Auth.AdminAccess.CanAccess(role, "/support/" + TicketId));
    }

    [Theory]
    [InlineData("Seller", false, false)]
    [InlineData("Moderator", true, false)]
    [InlineData("Manager", true, true)]
    public async Task SupportWorkspace_OnlyShowsRoleAppropriateControls(string role, bool notes, bool customerHistory)
    {
        var html = await Render<TicketDetailsPage>(new() { ["Id"] = TicketId }, role);
        Assert.Equal(notes, html.Contains("type=\"checkbox\""));
        Assert.Equal(customerHistory, html.Contains("/customer-activity/"));
        Assert.False(AdminPanel.Auth.AdminAccess.CanAccess("Customer", "/support"));
    }

    [Theory]
    [InlineData("Manager", true, true)]
    [InlineData("Administrator", true, true)]
    [InlineData("Manager", false, false)]
    [InlineData("Seller", true, false)]
    [InlineData("Moderator", true, false)]
    public async Task ChatDeleteControls_RequireTheFeatureAndAnOperationsRole(string role, bool enabled, bool visible)
    {
        var details = await Render<TicketDetailsPage>(new() { ["Id"] = TicketId }, role, enabled, true);
        Assert.Equal(visible, details.Contains("feature-delete"));
        var list = await Render<SupportManagement>([], role, enabled, true);
        Assert.Equal(visible, list.Contains("feature-delete"));
        Assert.Equal(role is "Administrator" or "Manager", list.Contains("/support/trash"));
    }

    [Theory]
    [InlineData("uk", "Функції", "Видалення чатів")]
    [InlineData("en", "Features", "Chat deletion")]
    public async Task FeatureSettingsAndTrash_RenderLocalizedSwitchAndRecovery(
        string culture,
        string title,
        string label
    )
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var settings = await Render<Features>([], "Administrator");
            Assert.Contains(title, settings);
            Assert.Contains(label, settings);
            Assert.Contains("role=\"switch\"", settings);
            Assert.Contains("aria-checked=\"false\"", settings);
            Assert.DoesNotContain("FeatureChatDeletionDescription", settings);
            var trash = await Render<DeletedChats>([], "Manager");
            Assert.Contains("Return fixture", trash);
            Assert.Contains("bi-arrow-counterclockwise", trash);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static async Task<string> Render<T>(
        Dictionary<string, object?> parameters,
        string role = "Manager",
        bool deletionEnabled = false,
        bool personalChat = false
    )
        where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<AuthenticationStateProvider>(new FixtureAuthentication(role));
        services.AddSingleton<NavigationManager>(new FixtureNavigation());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<UiTime>();
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton(
            new HttpClient(new FixtureApi(role, deletionEnabled, personalChat))
            {
                BaseAddress = new("https://fixture.invalid/"),
            }
        );
        services.AddSingleton<OperationsApiClient>();
        services.AddSingleton<CatalogApiClient>();
        services.AddSingleton<SettingsApiClient>();
        services.AddSingleton<CatalogModerationState>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            WebUtility.HtmlDecode(
                (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString()
            )
        );
    }

    private sealed class FixtureApi(string role, bool deletionEnabled, bool personalChat) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/support/" + TicketId + "/read" && request.Method == HttpMethod.Post)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            object value = path switch
            {
                "/api/v1/support/capabilities" => new TicketCapabilities(
                    deletionEnabled && role is "Administrator" or "Manager",
                    role is "Administrator" or "Manager"
                ),
                "/api/v1/support" => new Contracts.Common.PagedResponse<TicketRow>(
                    [personalChat ? Ticket with { Kind = "CHAT", Status = "OPEN" } : Ticket],
                    1,
                    12,
                    1,
                    1
                ),
                "/api/v1/support/deleted" => new Contracts.Common.PagedResponse<TicketRow>(
                    [Ticket with { DeletedAt = DateTimeOffset.UtcNow }],
                    1,
                    12,
                    1,
                    1
                ),
                "/api/v1/admin/settings/features" => new List<FeatureFlagDto>
                {
                    new(FeatureFlag.ChatDeletion, deletionEnabled, Guid.Empty, null),
                },
                "/api/v1/admin/catalog/filter-options" => new Contracts.Catalog.CatalogFilterOptionsDto([], [], []),
                "/api/v1/admin/catalog/products" => new Contracts.Common.PagedResponse<CatalogProductDto>(
                    [
                        new(
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            "Moderation fixture product",
                            "moderation-fixture",
                            "Electronics",
                            "Treba",
                            "PENDING_MODERATION",
                            "Summary",
                            "Description",
                            "FIXTURE",
                            100,
                            null,
                            5,
                            12,
                            "Ukraine",
                            DateTime.UtcNow,
                            []
                        ),
                    ],
                    1,
                    12,
                    1,
                    1
                ),
                "/api/v1/admin/catalog/moderation/pending-count" => 1,
                _ when path.EndsWith("/moderation-history") => new List<ProductModerationDto>(),
                _ when path.EndsWith("/resubmission-changes") => new ModerationComparisonDto(null, []),
                "/api/v1/admin/operations/metrics" => new OperationsMetrics(30, 1, 0, 250, 1, 1, 0, 0),
                _ when path == "/api/v1/admin/orders/" + OrderId => new OrderDetails(
                    Order,
                    "+3800000000",
                    "fixture@example.test",
                    null,
                    200,
                    0,
                    50,
                    null,
                    [
                        new(
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            "Product Fixture",
                            "Main",
                            "UI-SKU",
                            2,
                            100,
                            0,
                            200
                        ),
                    ],
                    [],
                    "version",
                    ["PAID", "CANCELLED"],
                    false
                ),
                _ when path == "/api/v1/support/" + TicketId => new TicketDetails(
                    personalChat ? Ticket with { Kind = "CHAT", Status = "OPEN" } : Ticket,
                    [new(Guid.NewGuid(), "Staff Fixture", "Manager", "Goods in transit", false, DateTimeOffset.UtcNow)],
                    ["CLOSED"]
                ),
                _ when path == "/api/v1/admin/operations/customers/" + CustomerId => new CustomerDetails(
                    new(CustomerId, "Customer Fixture", "fixture@example.test", 1, 250, 0, DateTimeOffset.UtcNow),
                    [Order],
                    [],
                    [Ticket]
                ),
                _ => throw new InvalidOperationException("Unexpected fixture route: " + path),
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) }
            );
        }
    }

    private sealed class FixtureAuthentication(string role) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(
                new AuthenticationState(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Fixture"))
                )
            );
    }

    private sealed class FixtureNavigation : NavigationManager
    {
        public FixtureNavigation() => Initialize("https://fixture.invalid/", "https://fixture.invalid/orders");

        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
