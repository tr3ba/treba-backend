using Application.Administration;
using Contracts.Admin.Operations;
using Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController, Route("api/v1/admin/promotions"), Authorize(Roles = "Administrator,Manager")]
public sealed class AdminPromotionsController(PromotionManagementService promotions) : ControllerBase
{
    [HttpGet]
    public Task<PagedResponse<PromotionView>> List(int page = 1, string? search = null, string? status = null, CancellationToken ct = default) => promotions.ListAsync(page, search, status, ct);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PromotionView>> Get(Guid id, CancellationToken ct) => await promotions.GetAsync(id, ct) is { } result ? Ok(result) : NotFound();
    [HttpPost]
    public async Task<ActionResult<PromotionView>> Create(PromotionInput request, CancellationToken ct)
    {
        var result = await promotions.SaveAsync(null, request, ct);
        return CreatedAtAction(nameof(Get), new
        {
            id = result.Id
        }, result);
    }
    [HttpPut("{id:guid}")]
    public Task<PromotionView> Update(Guid id, PromotionInput request, CancellationToken ct) => promotions.SaveAsync(id, request, ct);
}
