using System.Security.Claims;
using Application.Abstractions;
using Contracts.Admin.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public UsersController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Недействительный токен пользователя." });
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = "Пользователь не найден." });
        }

        var response = new UserDetailsResponse(
            user.Id,
            user.Email,
            user.Phone,
            user.FirstName,
            user.LastName,
            user.MiddleName,
            user.Status.ToString(),
            user.EmailVerified,
            user.PhoneVerified,
            user.CreatedAt,
            user.UpdatedAt,
            user.LastLoginAt,
            user.Profile is null
                ? null
                : new UserProfileResponse(
                    user.Profile.BirthDate,
                    user.Profile.Gender,
                    user.Profile.AvatarUrl,
                    user.Profile.Language,
                    user.Profile.MarketingEmailsEnabled),
            user.Addresses
                .Select(a => new UserAddressResponse(
                    a.Id,
                    a.Country,
                    a.Region,
                    a.City,
                    a.Street,
                    a.Building,
                    a.Apartment,
                    a.PostalCode,
                    a.RecipientName,
                    a.RecipientPhone,
                    a.DefaultAddress))
                .ToList());

        return Ok(response);
    }
}