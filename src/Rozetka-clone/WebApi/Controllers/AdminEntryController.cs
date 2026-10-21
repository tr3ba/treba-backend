using System.Security.Claims;
using Contracts.Authentication;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace WebApi.Controllers;

[ApiController, Route("api/auth/admin-entry"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminEntryController(AdminEntryService entry) : ControllerBase
{
    [HttpPost, Authorize(Roles = "Administrator,Manager,Moderator,Seller"), EnableRateLimiting("telegram-sensitive")]
    public Task<AdminEntryResponse> Create(CancellationToken ct) => entry.CreateAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct);

    [HttpPost("redeem"), AllowAnonymous, EnableRateLimiting("telegram-sensitive")]
    public Task<AuthResponse> Redeem(RedeemAdminEntryRequest request, CancellationToken ct) => entry.RedeemAsync(request.Code, ct);
}
