using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public static class TwoFactorMethods
{
    public const string Authenticator = "authenticator";
    public const string Email = "email";
}

public sealed record LoginResponse(
    AuthResponse? Authentication,
    TwoFactorChallengeResponse? Challenge
);

public sealed record TwoFactorChallengeResponse(
    Guid ChallengeId,
    IReadOnlyList<string> Methods,
    string MaskedEmail,
    DateTimeOffset ExpiresAt
);

public sealed record SendEmailLoginCodeRequest(
    Guid ChallengeId
);

public sealed record VerifyLoginSecondFactorRequest(
    Guid ChallengeId,
    [Required, RegularExpression("^(authenticator|email)$")] string Method,
    [Required, RegularExpression("^[0-9]{6}$")] string Code
);

public sealed record AccountSecuritySettingsResponse(
    bool AuthenticatorEnabled,
    DateTimeOffset? AuthenticatorEnabledAt,
    bool EmailVerified,
    bool EmailTwoFactorEnabled,
    string MaskedEmail
);

public sealed record AuthenticatorSetupResponse(
    string ManualKey,
    string QrCodeDataUrl,
    string AccountName,
    string Issuer
);

public sealed record ConfirmAuthenticatorRequest(
    [Required, RegularExpression("^[0-9]{6}$")] string Code
);

public sealed record StartEmailTwoFactorResponse(
    Guid ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt
);

public sealed record ConfirmEmailTwoFactorRequest(
    Guid ChallengeId,
    [Required, RegularExpression("^[0-9]{6}$")] string Code
);
