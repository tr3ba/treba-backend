using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public sealed record RefreshTokenRequest(
    [Required, StringLength(500)] string RefreshToken
);
