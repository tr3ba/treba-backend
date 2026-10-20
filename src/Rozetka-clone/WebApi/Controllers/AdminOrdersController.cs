using Application.Orders;
using Contracts.Admin.Operations;
using Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/admin/orders"), Authorize(Roles = "Administrator,Manager,Seller")]
public sealed class AdminOrdersController(OrderService orders) : ControllerBase
{
    [HttpGet]
    public Task<PagedResponse<OrderRow>> List(int page = 1, int size = 20, string? status = null, string? search = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, Guid? customerId = null, CancellationToken ct = default) =>
        orders.ListAsync(page, size, status, search, from, to, customerId, ct);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDetails>> Details(Guid id, CancellationToken ct) => await orders.DetailsAsync(id, ct) is { } result ? Ok(result) : NotFound();
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, ChangeOrderStatusRequest request, CancellationToken ct)
    {
        await orders.ChangeStatusAsync(id, request, ct);
        return NoContent();
    }
}
