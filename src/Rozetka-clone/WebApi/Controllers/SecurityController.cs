using System.Security.Claims;
using Application.Abstractions;
using Contracts.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/security")]
public sealed class SecurityController(
    ISecondFactorService secondFactorService
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AccountSecuritySettingsResponse), StatusCodes.Status200OK)]
    public Task<AccountSecuritySettingsResponse> Get(
        CancellationToken cancellationToken
    )
    {
        return secondFactorService.GetSettingsAsync(
            UserId,
            cancellationToken
        );
    }

    [HttpPost("authenticator/setup")]
    [ProducesResponseType(typeof(AuthenticatorSetupResponse), StatusCodes.Status200OK)]
    public Task<AuthenticatorSetupResponse> BeginAuthenticator(
        CancellationToken cancellationToken
    )
    {
        return secondFactorService.BeginAuthenticatorSetupAsync(
            UserId,
            cancellationToken
        );
    }

    [HttpPost("authenticator/confirm")]
    [ProducesResponseType(typeof(AccountSecuritySettingsResponse), StatusCodes.Status200OK)]
    public Task<AccountSecuritySettingsResponse> ConfirmAuthenticator(
        [FromBody] ConfirmAuthenticatorRequest request,
        CancellationToken cancellationToken
    )
    {
        return secondFactorService.ConfirmAuthenticatorAsync(
            UserId,
            request.Code,
            cancellationToken
        );
    }

    [HttpPost("authenticator/disable")]
    [ProducesResponseType(typeof(AccountSecuritySettingsResponse), StatusCodes.Status200OK)]
    public Task<AccountSecuritySettingsResponse> DisableAuthenticator(
        [FromBody] ConfirmAuthenticatorRequest request,
        CancellationToken cancellationToken
    )
    {
        return secondFactorService.DisableAuthenticatorAsync(
            UserId,
            request.Code,
            cancellationToken
        );
    }

    [HttpPost("email/setup")]
    [ProducesResponseType(typeof(StartEmailTwoFactorResponse), StatusCodes.Status200OK)]
    public Task<StartEmailTwoFactorResponse> BeginEmail(
        CancellationToken cancellationToken
    )
    {
        return secondFactorService.BeginEmailSetupAsync(
            UserId,
            cancellationToken
        );
    }

    [HttpPost("email/confirm")]
    [ProducesResponseType(typeof(AccountSecuritySettingsResponse), StatusCodes.Status200OK)]
    public Task<AccountSecuritySettingsResponse> ConfirmEmail(
        [FromBody] ConfirmEmailTwoFactorRequest request,
        CancellationToken cancellationToken
    )
    {
        return secondFactorService.ConfirmEmailSetupAsync(
            UserId,
            request.ChallengeId,
            request.Code,
            cancellationToken
        );
    }

    [HttpPost("email/disable/start")]
    [ProducesResponseType(typeof(StartEmailTwoFactorResponse), StatusCodes.Status200OK)]
    public Task<StartEmailTwoFactorResponse> BeginEmailDisable(
        CancellationToken cancellationToken
    )
    {
        return secondFactorService.BeginEmailDisableAsync(
            UserId,
            cancellationToken
        );
    }

    [HttpPost("email/disable/confirm")]
    [ProducesResponseType(typeof(AccountSecuritySettingsResponse), StatusCodes.Status200OK)]
    public Task<AccountSecuritySettingsResponse> ConfirmEmailDisable(
        [FromBody] ConfirmEmailTwoFactorRequest request,
        CancellationToken cancellationToken
    )
    {
        return secondFactorService.ConfirmEmailDisableAsync(
            UserId,
            request.ChallengeId,
            request.Code,
            cancellationToken
        );
    }

    private Guid UserId
    {
        get
        {
            return Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId
            )
                ? userId
                : throw new UnauthorizedAccessException("The access token does not contain a valid user identifier.");
        }
    }
}
