using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(72, MinimumLength = 8)] string Password,
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName,
    [StringLength(30)] string? PhoneNumber,
    string? RoleName = null
);
