using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public sealed record RequestPasswordResetRequest(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, RegularExpression("^(email|sms)$")] string VerificationMethod);

public sealed record PasswordResetChallengeResponse(
    Guid ChallengeId, string Method, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt,
    string Message);

public sealed record CompletePasswordResetRequest(
    [Required] Guid ChallengeId,
    [Required, RegularExpression("^[0-9]{6}$")] string Code,
    [Required, StringLength(72, MinimumLength = 8)] string NewPassword);
