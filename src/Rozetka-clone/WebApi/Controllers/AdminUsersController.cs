using Application.Abstractions;
using Contracts.Admin.Users;
using Contracts.Common;
using Domain.Entities.Users;
using DomainUser = Domain.Entities.Users.User;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Controllers;

[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IApplicationDbContext _dbContext;

    public AdminUsersController(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpPost]
    [ProducesResponseType<UserDetailsResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetailsResponse>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            ModelState.AddModelError(nameof(request.Email), "Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            ModelState.AddModelError(nameof(request.FirstName), "First name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            ModelState.AddModelError(nameof(request.LastName), "Last name is required.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Email == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A user with this email already exists.",
                Extensions = { ["code"] = "USER_EMAIL_EXISTS" }
            });
        }

        var user = DomainUser.Create(
            Guid.NewGuid(),
            request.Email,
            request.Phone,
            request.FirstName,
            request.LastName,
            request.MiddleName);

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = MapDetails(user);

        return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, response);
    }

    [HttpGet]
    [ProducesResponseType<PagedResponse<UserListItemResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<UserListItemResponse>>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int size = DefaultPageSize,
        [FromQuery(Name = "query")] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        size = Math.Clamp(size, 1, MaxPageSize);

        var usersQuery = _dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();

            usersQuery = usersQuery.Where(user =>
                user.Email.ToLower().Contains(normalizedSearch) ||
                user.FirstName.ToLower().Contains(normalizedSearch) ||
                user.LastName.ToLower().Contains(normalizedSearch) ||
                (user.Phone != null && user.Phone.Contains(normalizedSearch)));
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<UserStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            usersQuery = usersQuery.Where(user => user.Status == parsedStatus);
        }

        var totalElements = await usersQuery.LongCountAsync(cancellationToken);
        var totalPages = totalElements == 0
            ? 0
            : (int)Math.Ceiling(totalElements / (double)size);

        var users = await usersQuery
            .OrderByDescending(user => user.CreatedAt)
            .ThenBy(user => user.Email)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(user => new UserListItemResponse(
                user.Id,
                user.Email,
                user.Phone,
                user.FirstName,
                user.LastName,
                user.Status.ToString(),
                user.CreatedAt,
                user.LastLoginAt))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<UserListItemResponse>(
            users,
            page,
            size,
            totalElements,
            totalPages));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailsResponse>> GetUserById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new UserDetailsResponse(
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
                user.Profile == null
                    ? null
                    : new UserProfileResponse(
                        user.Profile.BirthDate,
                        user.Profile.Gender,
                        user.Profile.AvatarUrl,
                        user.Profile.Language,
                        user.Profile.MarketingEmailsEnabled),
                user.Addresses
                    .OrderByDescending(address => address.DefaultAddress)
                    .ThenBy(address => address.City)
                    .Select(address => new UserAddressResponse(
                        address.Id,
                        address.Country,
                        address.Region,
                        address.City,
                        address.Street,
                        address.Building,
                        address.Apartment,
                        address.PostalCode,
                        address.RecipientName,
                        address.RecipientPhone,
                        address.DefaultAddress))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost("{id:guid}/block")]
    [ProducesResponseType<UserStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<UserStatusResponse>> BlockUser(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, user => user.Block(), cancellationToken);

    [HttpPost("{id:guid}/unblock")]
    [ProducesResponseType<UserStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<UserStatusResponse>> UnblockUser(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, user => user.Unblock(), cancellationToken);

    private async Task<ActionResult<UserStatusResponse>> ChangeStatusAsync(
        Guid id,
        Action<DomainUser> changeStatus,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        changeStatus(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new UserStatusResponse(user.Id, user.Status.ToString()));
    }

    private static UserDetailsResponse MapDetails(DomainUser user) => new(
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
        null,
        []);
}
