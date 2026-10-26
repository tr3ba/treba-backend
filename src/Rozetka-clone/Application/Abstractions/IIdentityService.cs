using System;
using System.Threading;
using System.Threading.Tasks;
using Contracts.Authentication;

namespace Application.Abstractions;

public interface IIdentityService
{
    Task<LoginResponse> LoginWithGuardAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default
    );

    Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default
    );

    Task<AuthResponse> CompleteSecondFactorLoginAsync(
        VerifyLoginSecondFactorRequest request,
        CancellationToken cancellationToken = default
    );

    Task<AuthResponse> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default
    );

    Task RevokeTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    );

    Task<Guid> CreateUserAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default
    );

    Task<bool> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default
    );

    Task ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsInRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default
    );

    Task AddToRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default
    );

    Task<AuthResponse> ExternalLoginAsync(
        string provider,
        string accessToken,
        CancellationToken cancellationToken = default
    );

    Task<bool> LinkExternalProviderAsync(
        Guid userId,
        string provider,
        string providerUserId,
        CancellationToken cancellationToken = default
    );

    Task<bool> UnlinkExternalProviderAsync(
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default
    );
}
