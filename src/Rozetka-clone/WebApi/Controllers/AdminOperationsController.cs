using Application.Administration;
using Contracts.Admin.Operations;
using Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/admin/operations"), Authorize(Roles = "Administrator,Manager")]
public sealed class AdminOperationsController(OperationsService operations) : ControllerBase
{
    [HttpGet("metrics")]
    public Task<OperationsMetrics> Metrics(int days = 30, CancellationToken ct = default) => operations.MetricsAsync(days, ct);
    [HttpGet("customers")]
    public Task<PagedResponse<CustomerRow>> Customers(int page = 1, string? search = null, CancellationToken ct = default) => operations.CustomersAsync(page, search, ct);
    [HttpGet("customers/{id:guid}")]
    public async Task<ActionResult<CustomerDetails>> Customer(Guid id, CancellationToken ct) => await operations.CustomerAsync(id, ct) is { } result ? Ok(result) : NotFound();
}
