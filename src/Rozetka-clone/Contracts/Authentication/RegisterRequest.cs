namespace Contracts.Authentication;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string? RoleName = null);