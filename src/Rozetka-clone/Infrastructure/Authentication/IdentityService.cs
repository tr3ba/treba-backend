using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Authentication;

public sealed class IdentityService : IIdentityService
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;

    public IdentityService(IApplicationDbContext context, ITokenService tokenService)
    {
        _context = context;
        _tokenService = tokenService;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var emailNormalized = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _context.Users
            .AnyAsync(u => u.Email == emailNormalized, cancellationToken);

        if (existingUser)
        {
            throw new InvalidOperationException($"Пользователь с email '{request.Email}' уже существует.");
        }

        var targetRoleName = string.IsNullOrWhiteSpace(request.RoleName) ? Roles.Customer : request.RoleName;
        var role = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == targetRoleName, cancellationToken)
            ?? throw new InvalidOperationException($"Роль '{targetRoleName}' не найдена в системе.");

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = User.Create(
            Guid.NewGuid(),
            emailNormalized,
            request.PhoneNumber,
            request.FirstName,
            request.LastName
        );

        user.SetPasswordHash(passwordHash);
        user.AssignRole(role.Id);
        
        user.VerifyEmail();

        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenExpiry = DateTimeOffset.UtcNow.AddDays(7);
        user.SetRefreshToken(refreshToken, refreshTokenExpiry);

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user, role.Name);
        var expiresAt = _tokenService.GetAccessTokenExpiration();

        return new AuthResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            role.Name,
            accessToken,
            refreshToken,
            expiresAt);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var emailNormalized = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == emailNormalized, cancellationToken);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Неверный email или пароль.");
        }

        if (user.Status == UserStatus.Blocked || user.Status == UserStatus.Deleted)
        {
            throw new InvalidOperationException("Учетная запись заблокирована или удалена.");
        }

        user.RegisterLogin();

        var roleName = user.Role?.Name ?? Roles.Customer;

        var refreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenExpiry = DateTimeOffset.UtcNow.AddDays(7);
        user.SetRefreshToken(refreshToken, refreshTokenExpiry);

        await _context.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user, roleName);
        var expiresAt = _tokenService.GetAccessTokenExpiration();

        return new AuthResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            roleName,
            accessToken,
            refreshToken,
            expiresAt);
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.RefreshToken == request.RefreshToken, cancellationToken);

        if (user is null || user.RefreshTokenExpiryTime <= DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedAccessException("Недействительный или просроченный refresh token.");
        }

        var roleName = user.Role?.Name ?? Roles.Customer;

        var newRefreshToken = _tokenService.GenerateRefreshToken();
        user.SetRefreshToken(newRefreshToken, DateTimeOffset.UtcNow.AddDays(7));

        await _context.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user, roleName);
        var expiresAt = _tokenService.GetAccessTokenExpiration();

        return new AuthResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            roleName,
            accessToken,
            newRefreshToken,
            expiresAt);
    }

    public async Task RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken, cancellationToken);

        if (user is not null)
        {
            user.RevokeRefreshToken();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<Guid> CreateUserAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var emailNormalized = email.Trim().ToLowerInvariant();

        var existingUser = await _context.Users.AnyAsync(u => u.Email == emailNormalized, cancellationToken);
        if (existingUser)
        {
            throw new InvalidOperationException($"Пользователь с email '{email}' уже существует.");
        }

        var customerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == Roles.Customer, cancellationToken)
            ?? throw new InvalidOperationException("Роль Customer не найдена.");

        var user = User.Create(
            Guid.NewGuid(),
            emailNormalized,
            null,
            "User",
            "User"
        );

        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(password));
        user.AssignRole(customerRole.Id);
        user.VerifyEmail();

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        return user.Id;
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is not null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("Пользователь не найден.");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
        {
            throw new InvalidOperationException("Текущий пароль неверен.");
        }

        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(newPassword));
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsInRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user?.Role?.Name.Equals(role, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public async Task AddToRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("Пользователь не найден.");

        var dbRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == role, cancellationToken)
            ?? throw new InvalidOperationException($"Роль '{role}' не найдена.");

        user.AssignRole(dbRole.Id);
        await _context.SaveChangesAsync(cancellationToken);
    }
}