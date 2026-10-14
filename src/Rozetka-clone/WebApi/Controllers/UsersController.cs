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
    private const long MaxAvatarBytes = 5 * 1024 * 1024;
    private readonly IApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public UsersController(
        IApplicationDbContext context,
        IWebHostEnvironment environment
    )
    {
        _context = context;
        _environment = environment;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser(
        CancellationToken cancellationToken
    )
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(
                new
                {
                    message = "Недействительный токен пользователя."
                }
            );
        }

        var user = await _context
            .Users
            .Include(u => u.Role)
            .Include(u => u.Profile)
            .Include(u => u.Addresses)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Id == userId,
                cancellationToken
            );

        if (user is null)
        {
            return NotFound(
                new
                {
                    message = "Пользователь не найден."
                }
            );
        }

        return Ok(ToResponse(user));
    }

    [HttpPatch("me")]
    [ProducesResponseType(typeof(UserDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCurrentUser(
        [FromBody] UpdateCurrentUserRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(
                new
                {
                    message = "Недействительный токен пользователя."
                }
            );
        }

        var user = await _context
            .Users
            .Include(u => u.Role)
            .Include(u => u.Profile)
            .Include(u => u.Addresses)
            .FirstOrDefaultAsync(
                u => u.Id == userId,
                cancellationToken
            );

        if (user is null)
        {
            return NotFound(
                new
                {
                    message = "Пользователь не найден."
                }
            );
        }

        var normalizedEmail = request.Email
            .Trim()
            .ToLowerInvariant();
        var emailIsUsed = await _context.Users.AnyAsync(
            u =>
                u.Id != userId
                && u.Email == normalizedEmail,
            cancellationToken
        );

        if (emailIsUsed)
        {
            return Conflict(
                new
                {
                    message = "Этот email уже используется другим пользователем."
                }
            );
        }

        try
        {
            user.ChangeEmail(normalizedEmail);
            user.UpdateProfile(
                request.FirstName,
                request.LastName,
                request.MiddleName,
                request.Phone
            );

            if (user.Profile is null)
            {
                user.CreateProfile(
                    request.BirthDate,
                    request.Gender,
                    null,
                    request.Language,
                    request.MarketingEmailsEnabled
                );
                _context.UserProfiles.Add(user.Profile!);
            }
            else
            {
                user.UpdateProfileDetails(
                    request.BirthDate,
                    request.Gender,
                    user.Profile.AvatarUrl,
                    request.Language,
                    request.MarketingEmailsEnabled
                );
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(
                new
                {
                    message = exception.Message
                }
            );
        }

        return Ok(ToResponse(user));
    }

    [HttpPost("me/avatar")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UserDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadCurrentUserAvatar(
        [FromForm] IFormFile avatar,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(
                new
                {
                    message = "Недействительный токен пользователя."
                }
            );
        }

        if (avatar.Length == 0)
        {
            return BadRequest(
                new
                {
                    message = "Оберіть непорожній файл зображення."
                }
            );
        }

        if (avatar.Length > MaxAvatarBytes)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new
                {
                    message = "Розмір аватарки не може перевищувати 5 МБ."
                }
            );
        }

        await using var imageBuffer = new MemoryStream((int)avatar.Length);
        await avatar.CopyToAsync(
            imageBuffer,
            cancellationToken
        );
        var imageBytes = imageBuffer.ToArray();
        var extension = DetectImageExtension(imageBytes);

        if (extension is null)
        {
            return BadRequest(
                new
                {
                    message = "Підтримуються лише справжні JPG, PNG, WEBP або GIF зображення."
                }
            );
        }

        var user = await _context
            .Users
            .Include(u => u.Role)
            .Include(u => u.Profile)
            .Include(u => u.Addresses)
            .FirstOrDefaultAsync(
                u => u.Id == userId,
                cancellationToken
            );

        if (user is null)
        {
            return NotFound(
                new
                {
                    message = "Пользователь не найден."
                }
            );
        }

        var oldAvatarUrl = user.Profile?.AvatarUrl;
        var userDirectoryName = userId.ToString("N");
        var fileName = $"{Guid.NewGuid():N}.{extension}";
        var avatarDirectory = Path.Combine(
            WebRootPath,
            "uploads",
            "avatars",
            userDirectoryName
        );
        Directory.CreateDirectory(avatarDirectory);
        var destinationPath = Path.Combine(
            avatarDirectory,
            fileName
        );
        await System.IO.File.WriteAllBytesAsync(
            destinationPath,
            imageBytes,
            cancellationToken
        );

        var avatarUrl = $"/uploads/avatars/{userDirectoryName}/{fileName}";
        try
        {
            if (user.Profile is null)
            {
                user.CreateProfile(avatarUrl: avatarUrl);
                _context.UserProfiles.Add(user.Profile!);
            }
            else
            {
                user.Profile.ChangeAvatar(avatarUrl);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            DeleteFileIfExists(destinationPath);
            throw;
        }

        DeleteStoredAvatar(
            oldAvatarUrl,
            userId
        );
        return Ok(ToResponse(user));
    }

    [HttpDelete("me/avatar")]
    [ProducesResponseType(typeof(UserDetailsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteCurrentUserAvatar(
        CancellationToken cancellationToken
    )
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(
                new
                {
                    message = "Недействительный токен пользователя."
                }
            );
        }

        var user = await _context
            .Users
            .Include(u => u.Role)
            .Include(u => u.Profile)
            .Include(u => u.Addresses)
            .FirstOrDefaultAsync(
                u => u.Id == userId,
                cancellationToken
            );

        if (user is null)
        {
            return NotFound(
                new
                {
                    message = "Пользователь не найден."
                }
            );
        }

        var oldAvatarUrl = user.Profile?.AvatarUrl;
        if (user.Profile is not null
            && oldAvatarUrl is not null)
        {
            user.Profile.ChangeAvatar(null);
            await _context.SaveChangesAsync(cancellationToken);
            DeleteStoredAvatar(
                oldAvatarUrl,
                userId
            );
        }

        return Ok(ToResponse(user));
    }

    private bool TryGetCurrentUserId(
        out Guid userId
    )
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(
            userIdClaim,
            out userId
        );
    }

    private string WebRootPath
    {
        get
        {
            return _environment.WebRootPath ?? Path.Combine(
                _environment.ContentRootPath,
                "wwwroot"
            );
        }
    }

    private static string? DetectImageExtension(
        byte[] bytes
    )
    {
        if (
            bytes.Length >= 8
            && bytes
                .AsSpan(
                    0,
                    8
                )
                .SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
        )
        {
            return "png";
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return "jpg";
        }

        if (bytes.Length >= 6
            && (
                System.Text.Encoding.ASCII.GetString(
                    bytes,
                    0,
                    6
                ) is "GIF87a" or "GIF89a"
            ))
        {
            return "gif";
        }

        if (
            bytes.Length >= 12
            && System.Text.Encoding.ASCII.GetString(
                bytes,
                0,
                4
            ) == "RIFF"
            && System.Text.Encoding.ASCII.GetString(
                bytes,
                8,
                4
            ) == "WEBP"
        )
        {
            return "webp";
        }

        return null;
    }

    private void DeleteStoredAvatar(
        string? avatarUrl,
        Guid userId
    )
    {
        var expectedPrefix = $"/uploads/avatars/{userId:N}/";
        if (
            string.IsNullOrWhiteSpace(avatarUrl)
            || !avatarUrl.StartsWith(
                expectedPrefix,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return;
        }

        var expectedDirectory =
            Path
                .GetFullPath(
                    Path.Combine(
                        WebRootPath,
                        "uploads",
                        "avatars",
                        userId.ToString("N")
                    )
                )
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(
            Path.Combine(
                WebRootPath,
                avatarUrl
                    .TrimStart('/')
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar
                    )
            )
        );
        if (path.StartsWith(
            expectedDirectory,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            DeleteFileIfExists(path);
        }
    }

    private static void DeleteFileIfExists(
        string path
    )
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (IOException)
        {

        }
        catch (UnauthorizedAccessException)
        {

        }
    }

    private static UserDetailsResponse ToResponse(
        Domain.Entities.Users.User user
    )
    {
        return new UserDetailsResponse(
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
                user.Profile.MarketingEmailsEnabled
            ),
            user.Addresses
                .Select(
                    a =>
                        new UserAddressResponse(
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
                            a.DefaultAddress
                        )
                )
                .ToList()
        );
    }
}
