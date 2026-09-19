namespace Contracts.Authentication;

public sealed record AuthResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);