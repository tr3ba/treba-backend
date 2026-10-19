using Application.Administration;
using Contracts.Admin.Dashboard;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Route("api/v1/admin/dashboard")]
[Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator}")]
public sealed class AdminDashboardController(AdminWorkspaceService workspace) : ControllerBase
{
    [HttpGet]
    public Task<AdminDashboardDto> Get(int days = 30, CancellationToken ct = default) =>
        workspace.DashboardAsync(days,
            User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.Manager),
            User.IsInRole(Roles.Administrator), ct);
}
