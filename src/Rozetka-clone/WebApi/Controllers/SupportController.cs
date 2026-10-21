using Application.Administration;
using Contracts.Admin.Operations;
using Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/support"), Authorize(Roles = "Administrator,Manager,Moderator,Seller,Customer")]
public sealed class SupportController(SupportService support) : ControllerBase
{
    [HttpGet]
    public Task<PagedResponse<TicketRow>> List(int page = 1, string? status = null, string? kind = null, string? search = null, string? view = null, Guid? productId = null, CancellationToken ct = default) => support.ListAsync(page, status, kind, search, ct, view, productId);
    [HttpGet("{id:guid}/messages")]
    public Task<TicketMessagePage> Messages(Guid id, Guid? before = null, CancellationToken ct = default) => support.MessagesAsync(id, before, ct);
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, ReadTicketRequest request, CancellationToken ct)
    { await support.MarkReadAsync(id, request, ct); return NoContent(); }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetails>> Get(Guid id, CancellationToken ct) => await support.DetailsAsync(id, ct) is { } result ? Ok(result) : NotFound();
    [HttpPost, Authorize(Roles = "Administrator,Manager,Customer")]
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
    [HttpPatch("{id:guid}/status"), Authorize(Roles = "Administrator,Manager,Moderator,Seller")]
    public async Task<IActionResult> Status(Guid id, ChangeTicketStatusRequest request, CancellationToken ct)
    {
        await support.ChangeStatusAsync(id, request, ct);
        return NoContent();
    }
}
