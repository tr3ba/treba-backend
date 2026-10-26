using Application.Marketplace;
using Contracts.Marketplace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController, Authorize, Route("api/v1/account"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MarketplaceAccountController(CustomerAccountService account, SellerApplicationService sellers, NotificationService notifications) : ControllerBase
{
    [HttpGet("preferences")] public Task<CustomerPreferencesView> Preferences(CancellationToken ct) => account.PreferencesAsync(ct);
    [HttpPatch("preferences")] public Task<CustomerPreferencesView> SavePreferences(SaveCustomerPreferencesRequest input, CancellationToken ct) => account.SavePreferencesAsync(input, ct);
    [HttpGet("orders")] public async Task<IActionResult> Orders(int page = 1, string? search = null, DateTimeOffset? from = null, CancellationToken ct = default) => Ok(await account.OrdersAsync(page, search, from, ct));
    [HttpGet("shipments")] public async Task<IActionResult> Shipments(int page = 1, string? search = null, CancellationToken ct = default) => Ok(await account.ShipmentsAsync(page, search, ct));
    [HttpGet("orders/{id:guid}")] public async Task<IActionResult> Order(Guid id, CancellationToken ct) => await account.OrderAsync(id, ct) is { } order ? Ok(order) : NotFound();
    [HttpGet("seller-application")] public async Task<IActionResult> Application(CancellationToken ct) => Ok(new { application = await sellers.MineAsync(ct) });
    [HttpPost("seller-application"), Authorize(Roles = "Customer")] public Task<SellerApplicationView> Apply(SellerApplicationInput input, CancellationToken ct) => sellers.ApplyAsync(input, ct);
    [HttpGet("notifications")] public Task<NotificationFeed> Notifications(CancellationToken ct) => notifications.FeedAsync(ct);
    [HttpPost("notifications/read")] public async Task<IActionResult> Read(ReadNotificationsRequest input, CancellationToken ct) { await notifications.ReadAsync(input.Keys, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/v1/account/notification-settings"), ResponseCache(NoStore = true)]
public sealed class CustomerNotificationSettingsController(CustomerNotificationSettingsService settings) : ControllerBase
{
    [HttpGet] public Task<NotificationSettingsView> Get(CancellationToken ct) => settings.GetAsync(ct);
    [HttpPut] public Task<NotificationSettingsView> Save(SaveNotificationSettingsRequest input, CancellationToken ct) => settings.SaveAsync(input, ct);
}

[ApiController, Authorize(Roles = "Administrator,Manager"), Route("api/v1/admin/seller-applications")]
public sealed class SellerApplicationsController(SellerApplicationService sellers) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(int page = 1, string? status = null, CancellationToken ct = default) => Ok(await sellers.ListAsync(page, status, ct));
    [HttpPost("{id:guid}/review")] public Task<SellerApplicationView> Review(Guid id, ReviewSellerApplicationRequest input, CancellationToken ct) => sellers.ReviewAsync(id, input, ct);
}
