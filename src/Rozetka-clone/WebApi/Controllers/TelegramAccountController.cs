using System.Security.Claims;
using System.Globalization;
using Contracts.Telegram;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebApi.Telegram;

namespace WebApi.Controllers;

[ApiController, Route("api/telegram"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TelegramAccountController(TelegramLinkService links, TelegramGuardService guard) : ControllerBase
{
    [HttpGet("/telegram/{timestamp}"), AllowAnonymous, ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult CorrectCopiedMessageTime(string timestamp) =>
        DateTime.TryParseExact(timestamp, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? LocalRedirect("/telegram/") : NotFound();

    [HttpGet("configuration"), AllowAnonymous]
    public TelegramConfigurationDto Configuration() => links.Configuration();
    [HttpGet("link"), Authorize(Roles = Roles.Moderator + "," + Roles.Seller)]
    public Task<TelegramLinkDto> Link(CancellationToken ct) => links.StatusAsync(UserId, ct);
    [HttpPost("link"), Authorize(Roles = Roles.Moderator + "," + Roles.Seller), EnableRateLimiting("telegram-sensitive")]
    public Task<TelegramLinkStartDto> BeginLink(CancellationToken ct) => links.BeginAsync(UserId, ct);
    [HttpPost("link/confirm"), Authorize(Roles = Roles.Moderator + "," + Roles.Seller)]
    public Task<TelegramLinkDto> ConfirmLink(ConfirmTelegramLinkRequest request, CancellationToken ct) => links.ConfirmAsync(UserId, request.ChallengeId, ct);
    [HttpDelete("link"), Authorize(Roles = Roles.Moderator + "," + Roles.Seller)]
    public async Task<IActionResult> Unlink(CancellationToken ct) { await links.UnlinkAsync(UserId, ct); return NoContent(); }
    [HttpPost("guard"), AllowAnonymous, EnableRateLimiting("telegram-sensitive")]
    public Task<GuardStartDto> StartGuard(CreateGuardRequest request, CancellationToken ct) => guard.CreateAsync(request.DeviceName, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
    [HttpPost("guard/status"), AllowAnonymous, EnableRateLimiting("telegram-poll")]
    public Task<GuardStatusDto> GuardStatus(GuardBrowserRequest request, CancellationToken ct) => guard.StatusAsync(request.Id, request.BrowserSecret, ct);
    [HttpPost("guard/complete"), AllowAnonymous, EnableRateLimiting("telegram-sensitive")]
    public async Task<IActionResult> CompleteGuard(GuardBrowserRequest request, CancellationToken ct) => Ok(await guard.CompleteAsync(request.Id, request.BrowserSecret, ct));
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
