using Contracts.Admin.Settings;
using Contracts.Common;
using Domain.Entities.Users;
using Infrastructure.Administration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Authorize(Roles = Roles.Administrator)]
[Route("api/v1/admin/settings")]
public sealed class AdminSettingsController(StaffSettingsService settings) : ControllerBase
{
    [HttpGet("staff")]
    public Task<PagedResponse<StaffDto>> Staff(int page = 1, string? search = null, string? role = null, CancellationToken ct = default) =>
        settings.ListAsync(page, search, role, ct);

    [HttpPost("staff")]
    public async Task<ActionResult<StaffDto>> Create(CreateStaffRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await settings.CreateAsync(request, ct));

    [HttpPut("staff/{id:guid}/access")]
    public async Task<ActionResult<StaffDto>> Update(Guid id, UpdateStaffAccessRequest request, CancellationToken ct) =>
        await settings.UpdateAsync(id, request, ct) is { } staff ? Ok(staff) : NotFound();

    [HttpGet("audit")]
    public Task<PagedResponse<AuditLogDto>> Audit(int page = 1, string? search = null, string? entityType = null, string? action = null,
        Guid? actorId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        settings.AuditAsync(page, search, entityType, action, actorId, from, to, ct);
}
