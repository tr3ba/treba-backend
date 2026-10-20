using Application.Administration;
using Contracts.Admin.Operations;
using Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/support"), Authorize(Roles = "Administrator,Manager,Customer")]
public sealed class SupportController(SupportService support) : ControllerBase
{
    [HttpGet]
    public Task<PagedResponse<TicketRow>> List(int page = 1, string? status = null, string? kind = null, string? search = null, string? view = null, CancellationToken ct = default) => support.ListAsync(page, status, kind, search, ct, view);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetails>> Get(Guid id, CancellationToken ct) => await support.DetailsAsync(id, ct) is { } result ? Ok(result) : NotFound();
    [HttpPost]
    public async Task<ActionResult<TicketDetails>> Create(CreateTicketRequest request, CancellationToken ct)
    {
        var result = await support.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new
        {
            id = result.Summary.Id
        }, result);
    }
    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> Message(Guid id, TicketMessageRequest request, CancellationToken ct)
    {
        await support.MessageAsync(id, request, ct);
        return NoContent();
    }
    [HttpPatch("{id:guid}/status"), Authorize(Roles = "Administrator,Manager")]
    public async Task<IActionResult> Status(Guid id, ChangeTicketStatusRequest request, CancellationToken ct)
    {
        await support.ChangeStatusAsync(id, request, ct);
        return NoContent();
    }
}
