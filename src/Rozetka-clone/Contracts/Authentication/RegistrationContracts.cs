using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public sealed record RegistrationMethodsResponse(IReadOnlyList<string> Methods);

public sealed record RegistrationChallengeResponse(
    Guid ChallengeId, string Method, string MaskedDestination,
    DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);

public sealed record VerifyRegistrationRequest(
    [Required] Guid ChallengeId,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);

public sealed record ResendRegistrationCodeRequest([Required] Guid ChallengeId);
