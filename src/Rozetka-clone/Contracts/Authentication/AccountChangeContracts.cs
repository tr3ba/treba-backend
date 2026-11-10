using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public sealed record BeginAccountChangeRequest([Required, MaxLength(320)] string Target,
    VerifyLoginSecondFactorRequest? SecondFactor = null);
public sealed record ConfirmEmailChangeRequest(Guid ChallengeId, [Required] string CurrentEmailCode,
    [Required] string NewEmailCode);
public sealed record AccountChangeChallengeResponse(Guid ChallengeId, DateTimeOffset ExpiresAt);
