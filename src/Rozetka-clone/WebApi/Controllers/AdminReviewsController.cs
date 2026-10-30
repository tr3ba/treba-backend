using Application.Administration;
using Application.Abstractions;
using Contracts.Admin.Settings;
using Application.Reviews;
using Contracts.Admin.Reviews;
using Contracts.Common;
using Domain.Entities.Users;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Route("api/v1/admin/reviews")]
[Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator}")]
public sealed class AdminReviewsController(AdminWorkspaceService workspace, IReviewService reviews, ReviewReplyTemplateService templates, ICurrentActor actor) : ControllerBase
{
    [HttpGet]
    public Task<PagedResponse<AdminReviewDto>> List(
        int page = 1, int size = 20, string? status = null, string? search = null, CancellationToken ct = default) =>
        workspace.ReviewsAsync(page, size, status, search, ct);

    [HttpGet("pending-count")]
    public Task<int> PendingCount(CancellationToken ct) => workspace.PendingReviewsAsync(ct);

    [HttpGet("reply-templates")]
    public Task<IReadOnlyList<ReviewReplyTemplateDto>> ReplyTemplates(CancellationToken ct) => templates.ListAsync(false, ct);

    [HttpPut("{id:guid}/moderate")]
    public async Task<IActionResult> Moderate(Guid id, ModerateReviewRequest request, CancellationToken ct)
    {
        var status = AdminWorkspaceService.ParseReviewStatus(request.Status);
        if (status is not (ReviewStatus.PUBLISHED or ReviewStatus.REJECTED))
            return BadRequest(new { message = "Choose PUBLISHED or REJECTED." });

        try { await reviews.ModerateReviewAsync(id, status, ct, request.Reply, actor.UserId); }
        catch (KeyNotFoundException) { return NotFound(); }
        return NoContent();
    }
}
