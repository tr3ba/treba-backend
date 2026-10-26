using Application.Abstractions;
using Application.Delivery;
using Contracts.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/checkout-delivery"), Authorize, EnableRateLimiting("delivery"), ResponseCache(NoStore = true)]
public sealed class CheckoutDeliveryController(ICheckoutDeliveryProvider provider, CheckoutDeliveryService delivery) : ControllerBase
{
    [HttpGet("capabilities")] public DeliveryCapabilities Capabilities() => delivery.Capabilities();
    [HttpGet("cities")] public Task<IReadOnlyList<DeliveryCity>> Cities(string query, CancellationToken ct) => provider.CitiesAsync(query, ct);
    [HttpGet("pickup-points")] public Task<DeliveryPickupPage> Points(Guid cityRef, string query = "", string kind = "all", int page = 1, CancellationToken ct = default) => provider.PickupPointsAsync(cityRef, query, kind, page, ct);
    [HttpPost("quotes")] public Task<DeliveryQuoteView> Quote(DeliveryQuoteRequest input, CancellationToken ct) => delivery.QuoteAsync(input, ct);
}
