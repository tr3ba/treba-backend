using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;

namespace Infrastructure.Authentication;

public sealed class IdentityService : IIdentityService
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ISecondFactorService _secondFactorService;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public IdentityService(
        IApplicationDbContext context,
        ITokenService tokenService,
        ISecondFactorService secondFactorService,
        IConfiguration configuration,
        HttpClient httpClient
    )
    {
        _context = context;
        _tokenService = tokenService;
        _secondFactorService = secondFactorService;
        _configuration = configuration;
        _httpClient = httpClient;
    }

    public async Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(request.RoleName)
            && request.RoleName != Roles.Customer)
        {
            throw new InvalidOperationException("Public registration only supports the Customer role.");
        }

        var emailNormalized = request.Email
            .Trim()
            .ToLowerInvariant();

        var existingUser = await _context.Users.AnyAsync(
            u => u.Email == emailNormalized,
            cancellationToken
        );

        if (existingUser)
        {
            throw new InvalidOperationException($"Користувач з електронною адресою '{request.Email}' уже існує.");
        }

        var targetRoleName = Roles.Customer;
        var role =
            await _context.Roles.FirstOrDefaultAsync(
                r => r.Name == targetRoleName,
                cancellationToken
            )
            ?? throw new InvalidOperationException($"Роль '{targetRoleName}' не знайдено в системі.");

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
        user.SetRefreshToken(
            refreshToken,
            refreshTokenExpiry
        );

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            user,
            role.Name
        );
        var expiresAt = _tokenService.GetAccessTokenExpiration();

        return new AuthResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            role.Name,
            accessToken,
            refreshToken,
            expiresAt
        );
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var emailNormalized = request.Email
            .Trim()
            .ToLowerInvariant();

        var user = await _context
            .Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(
                u => u.Email == emailNormalized,
                cancellationToken
            );

        if (
            user is null
            || string.IsNullOrEmpty(user.PasswordHash)
            || !BCrypt.Net.BCrypt.Verify(
                request.Password,
                user.PasswordHash
            )
        )
        {
            throw new UnauthorizedAccessException("Неправильна електронна адреса або пароль.");
        }

        if (user.Status == UserStatus.Blocked
            || user.Status == UserStatus.Deleted)
        {
            throw new InvalidOperationException("Обліковий запис заблоковано або видалено.");
        }

        var challenge = await _secondFactorService.CreateLoginChallengeAsync(
            user,
            cancellationToken
        );
        if (challenge is not null)
        {
            return new LoginResponse(
                null,
                challenge
            );
        }

        return new LoginResponse(
            await IssueLoginTokensAsync(
                user,
                cancellationToken
            ),
            null
        );
    }

    public async Task<LoginResponse> LoginWithGuardAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active || user.Role?.Name is not (Roles.Administrator or Roles.Manager or Roles.Moderator or Roles.Seller))
            throw new UnauthorizedAccessException("Guard доступний лише активним працівникам і продавцям.");
        // A linked and approved Guard is itself one of the account's verification methods.
        return new LoginResponse(await IssueLoginTokensAsync(user, cancellationToken), null);
    }

    public async Task<AuthResponse> CompleteSecondFactorLoginAsync(
        VerifyLoginSecondFactorRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var user = await _secondFactorService.VerifyLoginChallengeAsync(
            request,
            cancellationToken
        );
        return await IssueLoginTokensAsync(
            user,
            cancellationToken
        );
    }

    public async Task<AuthResponse> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var user = await _context
            .Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(
                u => u.RefreshToken == request.RefreshToken,
                cancellationToken
            );

        if (
            user is null
            || user.RefreshTokenExpiryTime is null
            || user.RefreshTokenExpiryTime <= DateTimeOffset.UtcNow
            || user.Status is UserStatus.Blocked or UserStatus.Deleted
        )
        {
            throw new UnauthorizedAccessException("Недійсний або прострочений токен оновлення.");
        }

        var roleName = user.Role?.Name ?? Roles.Customer;

        var newRefreshToken = _tokenService.GenerateRefreshToken();
        user.SetRefreshToken(
            newRefreshToken,
            DateTimeOffset.UtcNow.AddDays(7)
        );

        await _context.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            user,
            roleName
        );
        var expiresAt = _tokenService.GetAccessTokenExpiration();

        return new AuthResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            roleName,
            accessToken,
            newRefreshToken,
            expiresAt
        );
    }

    public async Task RevokeTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    )
    {
        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.RefreshToken == refreshToken,
            cancellationToken
        );

        if (user is not null)
        {
            user.RevokeRefreshToken();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<Guid> CreateUserAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        var emailNormalized = email
            .Trim()
            .ToLowerInvariant();

        var existingUser = await _context.Users.AnyAsync(
            u => u.Email == emailNormalized,
            cancellationToken
        );
        if (existingUser)
        {
            throw new InvalidOperationException($"Користувач з електронною адресою '{email}' уже існує.");
        }

        var customerRole =
            await _context.Roles.FirstOrDefaultAsync(
                r => r.Name == Roles.Customer,
                cancellationToken
            )
            ?? throw new InvalidOperationException("Роль Customer не знайдено.");

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

    public async Task<bool> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Id == userId,
            cancellationToken
        );
        return user is not null
            && BCrypt.Net.BCrypt.Verify(
                password,
                user.PasswordHash
            );
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default
    )
    {
        var user =
            await _context.Users.FirstOrDefaultAsync(
                u => u.Id == userId,
                cancellationToken
            )
            ?? throw new InvalidOperationException("Користувача не знайдено.");

        if (!BCrypt.Net.BCrypt.Verify(
            currentPassword,
            user.PasswordHash
        ))
        {
            throw new InvalidOperationException("Поточний пароль неправильний.");
        }

        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(newPassword));
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsInRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default
    )
    {
        var user = await _context
            .Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(
                u => u.Id == userId,
                cancellationToken
            );

        return user?.Role?.Name.Equals(
            role,
            StringComparison.OrdinalIgnoreCase
        ) ?? false;
    }

    public async Task AddToRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default
    )
    {
        var user =
            await _context.Users.FirstOrDefaultAsync(
                u => u.Id == userId,
                cancellationToken
            )
            ?? throw new InvalidOperationException("Користувача не знайдено.");

        var dbRole =
            await _context.Roles.FirstOrDefaultAsync(
                r => r.Name == role,
                cancellationToken
            )
            ?? throw new InvalidOperationException($"Роль '{role}' не знайдено.");

        user.AssignRole(dbRole.Id);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthResponse> ExternalLoginAsync(
        string provider,
        string accessToken,
        CancellationToken cancellationToken = default
    )
    {
        var (providerUserId, email, firstName, lastName, avatarUrl) = await GetExternalUserInfoAsync(provider, accessToken, cancellationToken);

        var existingLink = await _context.UserExternalLogins
            .Include(uel => uel.User)
            .ThenInclude(u => u.Role)
            .FirstOrDefaultAsync(
                uel => uel.Provider == provider && uel.ProviderUserId == providerUserId,
                cancellationToken
            );

        if (existingLink is not null)
        {
            var user = existingLink.User;
            if (user.Status == UserStatus.Blocked || user.Status == UserStatus.Deleted)
            {
                throw new InvalidOperationException("Обліковий запис заблоковано або видалено.");
            }

            if (!string.IsNullOrWhiteSpace(avatarUrl) && user.Profile is not null)
            {
                user.UpdateProfileDetails(
                    user.Profile.BirthDate,
                    user.Profile.Gender,
                    avatarUrl,
                    user.Profile.Language,
                    user.Profile.MarketingEmailsEnabled
                );
            }

            return await IssueLoginTokensAsync(user, cancellationToken);
        }

        var emailNormalized = email.Trim().ToLowerInvariant();
        var userByEmail = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == emailNormalized, cancellationToken);

        if (userByEmail is not null)
        {
            userByEmail.AddExternalLogin(provider, providerUserId);
            await _context.SaveChangesAsync(cancellationToken);
            return await IssueLoginTokensAsync(userByEmail, cancellationToken);
        }

        var customerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == Roles.Customer, cancellationToken)
            ?? throw new InvalidOperationException("Роль Customer не знайдено.");

        var newUser = User.Create(
            Guid.NewGuid(),
            emailNormalized,
            null,
            firstName,
            lastName
        );

        newUser.SetPasswordHash(Guid.NewGuid().ToString());
        newUser.AssignRole(customerRole.Id);
        newUser.VerifyEmail();
        newUser.AddExternalLogin(provider, providerUserId);

        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            newUser.CreateProfile(avatarUrl: avatarUrl);
        }

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync(cancellationToken);

        return await IssueLoginTokensAsync(newUser, cancellationToken);
    }

    public async Task<bool> LinkExternalProviderAsync(
        Guid userId,
        string provider,
        string providerUserId,
        CancellationToken cancellationToken = default
    )
    {
        var user = await _context.Users
            .Include(u => u.ExternalLogins)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException("Користувача не знайдено.");
        }

        if (user.ExternalLogins.Any(el => el.Provider == provider))
        {
            throw new InvalidOperationException($"Вже пов'язано з {provider}.");
        }

        var existingLink = await _context.UserExternalLogins
            .FirstOrDefaultAsync(uel => uel.Provider == provider && uel.ProviderUserId == providerUserId, cancellationToken);

        if (existingLink is not null)
        {
            throw new InvalidOperationException("Цей обліковий запис вже пов'язаний з іншим користувачем.");
        }

        user.AddExternalLogin(provider, providerUserId);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnlinkExternalProviderAsync(
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        var user = await _context.Users
            .Include(u => u.ExternalLogins)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException("Користувача не знайдено.");
        }

        var externalLogin = user.ExternalLogins.FirstOrDefault(el => el.Provider == provider);
        if (externalLogin is null)
        {
            throw new InvalidOperationException($"Обліковий запис {provider} не пов'язаний.");
        }

        if (string.IsNullOrEmpty(user.PasswordHash) && user.ExternalLogins.Count == 1)
        {
            throw new InvalidOperationException("Неможливо відв'язати єдиний спосіб входу. Встановіть пароль спочатку.");
        }

        user.RemoveExternalLogin(provider);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<(string ProviderUserId, string Email, string FirstName, string LastName, string? AvatarUrl)> GetExternalUserInfoAsync(
        string provider,
        string accessToken,
        CancellationToken cancellationToken)
    {
        return provider.ToLowerInvariant() switch
        {
            "google" => await GetGoogleUserInfoAsync(accessToken, cancellationToken),
            "facebook" => await GetFacebookUserInfoAsync(accessToken, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
        };
    }

    private async Task<(string ProviderUserId, string Email, string FirstName, string LastName, string? AvatarUrl)> GetGoogleUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var userInfo = JsonSerializer.Deserialize<GoogleUserInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (userInfo is null || string.IsNullOrEmpty(userInfo.Id) || string.IsNullOrEmpty(userInfo.Email))
        {
            throw new InvalidOperationException("Failed to get user info from Google.");
        }

        return (
            userInfo.Id,
            userInfo.Email,
            userInfo.GivenName ?? "User",
            userInfo.FamilyName ?? "",
            userInfo.Picture
        );
    }

    private async Task<(string ProviderUserId, string Email, string FirstName, string LastName, string? AvatarUrl)> GetFacebookUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.facebook.com/me?fields=id,email,first_name,last_name,picture.type(large)");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var userInfo = JsonSerializer.Deserialize<FacebookUserInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (userInfo is null || string.IsNullOrEmpty(userInfo.Id) || string.IsNullOrEmpty(userInfo.Email))
        {
            throw new InvalidOperationException("Failed to get user info from Facebook.");
        }

        return (
            userInfo.Id,
            userInfo.Email,
            userInfo.FirstName ?? "User",
            userInfo.LastName ?? "",
            userInfo.Picture?.Data?.Url
        );
    }

    private sealed record GoogleUserInfo(
        string Id,
        string Email,
        string? GivenName,
        string? FamilyName,
        string? Picture,
        bool VerifiedEmail
    );

    private sealed record FacebookUserInfo(
        string Id,
        string Email,
        string? FirstName,
        string? LastName,
        FacebookPicture? Picture
    );

    private sealed record FacebookPicture(FacebookPictureData? Data);

    private sealed record FacebookPictureData(string? Url);

    private async Task<AuthResponse> IssueLoginTokensAsync(
        User user,
        CancellationToken cancellationToken
    )
    {
        user.RegisterLogin();
        var roleName = user.Role?.Name ?? Roles.Customer;
        var refreshToken = _tokenService.GenerateRefreshToken();
        user.SetRefreshToken(
            refreshToken,
            DateTimeOffset.UtcNow.AddDays(7)
        );
        await _context.SaveChangesAsync(cancellationToken);
        return new AuthResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            roleName,
            _tokenService.GenerateAccessToken(
                user,
                roleName
            ),
            refreshToken,
            _tokenService.GetAccessTokenExpiration()
        );
    }
}
