using Contracts.Authentication;
using Domain.Entities.Users;

namespace Application.Abstractions;

public interface ISecondFactorService
{
    Task<TwoFactorChallengeResponse?> CreateLoginChallengeAsync(
        User user,
        CancellationToken cancellationToken = default
    );
    Task SendLoginEmailCodeAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default
    );
    Task<User> VerifyLoginChallengeAsync(
        VerifyLoginSecondFactorRequest request,
        CancellationToken cancellationToken = default
    );
    Task<AccountSecuritySettingsResponse> GetSettingsAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );
    Task<AuthenticatorSetupResponse> BeginAuthenticatorSetupAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );
    Task<AccountSecuritySettingsResponse> ConfirmAuthenticatorAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default
    );
    Task<AccountSecuritySettingsResponse> DisableAuthenticatorAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default
    );
    Task<StartEmailTwoFactorResponse> BeginEmailSetupAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );
    Task<AccountSecuritySettingsResponse> ConfirmEmailSetupAsync(
        Guid userId,
        Guid challengeId,
        string code,
        CancellationToken cancellationToken = default
    );
    Task<StartEmailTwoFactorResponse> BeginEmailDisableAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );
    Task<AccountSecuritySettingsResponse> ConfirmEmailDisableAsync(
        Guid userId,
        Guid challengeId,
        string code,
        CancellationToken cancellationToken = default
    );
}

public interface IEmailSender
{
    Task SendSecurityCodeAsync(
        string recipient,
        string displayName,
        string code,
        TimeSpan validFor,
        CancellationToken cancellationToken = default
    );
}
