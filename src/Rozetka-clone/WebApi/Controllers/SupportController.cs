using Application.Administration;
using Contracts.Admin.Operations;
using Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/support"), Authorize(Roles = "Administrator,Manager,Moderator,Seller,Customer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportController(SupportService support) : ControllerBase
{
    [HttpGet("capabilities")]
    public Task<TicketCapabilities> Capabilities(CancellationToken ct) => support.CapabilitiesAsync(ct);
    [HttpGet("deleted"), Authorize(Roles = "Administrator,Manager")]
    public Task<PagedResponse<TicketRow>> Deleted(int page = 1, string? search = null, CancellationToken ct = default) => support.DeletedAsync(page, search, ct);
    [HttpDelete("{id:guid}"), Authorize(Roles = "Administrator,Manager")]
    public async Task<IActionResult> Delete(Guid id, TicketDeletionRequest request, CancellationToken ct)
    { await support.DeleteAsync(id, request, ct); return NoContent(); }
    [HttpPost("{id:guid}/restore"), Authorize(Roles = "Administrator,Manager")]
    public async Task<IActionResult> Restore(Guid id, TicketDeletionRequest request, CancellationToken ct)
    { await support.RestoreAsync(id, request, ct); return NoContent(); }
    [HttpGet]
    public Task<PagedResponse<TicketRow>> List(int page = 1, string? status = null, string? kind = null, string? search = null, string? view = null, Guid? productId = null, Guid? orderId = null, CancellationToken ct = default) => support.ListAsync(page, status, kind, search, ct, view, productId, orderId);
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
