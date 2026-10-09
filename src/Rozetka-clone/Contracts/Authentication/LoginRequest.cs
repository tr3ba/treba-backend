using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public sealed record LoginRequest(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(72)] string Password
);
