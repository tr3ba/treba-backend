using Application.Abstractions;
using Contracts.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IIdentityService _identityService;
    private readonly ISecondFactorService _secondFactorService;

    public AuthController(
        IIdentityService identityService,
        ISecondFactorService secondFactorService
    )
    {
        _identityService = identityService;
        _secondFactorService = secondFactorService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var response = await _identityService.RegisterAsync(
                request,
                cancellationToken
            );
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
    }

    [HttpPost("external")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ExternalLogin(
        [FromBody] ExternalLoginRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var response = await _identityService.ExternalLoginAsync(
                request.Provider,
                request.AccessToken,
                cancellationToken
            );
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(
                new
                {
                    message = ex.Message
                }
            );
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(
                new
                {
                    message = $"Failed to validate token with {request.Provider}: {ex.Message}"
                }
            );
        }
    }

    [HttpPost("external/link")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LinkExternalProvider(
        [FromBody] ExternalLoginRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

            var result = await _identityService.LinkExternalProviderAsync(
                userId,
                request.Provider,
                request.AccessToken,
                cancellationToken
            );

            return Ok(new { success = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(
                new
                {
                    message = "Unauthorized"
                }
            );
        }
    }

    [HttpPost("external/unlink")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnlinkExternalProvider(
        [FromBody] UnlinkExternalProviderRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

            var result = await _identityService.UnlinkExternalProviderAsync(
                userId,
                request.Provider,
                cancellationToken
            );

            return Ok(new { success = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(
                new
                {
                    message = "Unauthorized"
                }
            );
        }
    }

    [HttpPost("2fa/email/send")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SendEmailCode(
        [FromBody] SendEmailLoginCodeRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _secondFactorService.SendLoginEmailCodeAsync(
                request.ChallengeId,
                cancellationToken
            );
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(
                new
                {
                    message = ex.Message
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
    }

    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifySecondFactor(
        [FromBody] VerifyLoginSecondFactorRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Ok(
                await _identityService.CompleteSecondFactorLoginAsync(
                    request,
                    cancellationToken
                )
            );
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(
                new
                {
                    message = ex.Message
                }
            );
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var response = await _identityService.LoginAsync(
                request,
                cancellationToken
            );
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(
                new
                {
                    message = ex.Message
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var response = await _identityService.RefreshTokenAsync(
                request,
                cancellationToken
            );
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(
                new
                {
                    message = ex.Message
                }
            );
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken
    )
    {
        await _identityService.RevokeTokenAsync(
            request.RefreshToken,
            cancellationToken
        );
        return NoContent();
    }
}

public sealed record UnlinkExternalProviderRequest(
    string Provider
);
