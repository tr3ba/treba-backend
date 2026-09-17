namespace Contracts.Admin.Users;

public sealed record UserListItemResponse(
    Guid Id,
    string Email,
    string? Phone,
    string FirstName,
    string LastName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UserDetailsResponse(
    Guid Id,
    string Email,
    string? Phone,
    string FirstName,
    string LastName,
    string? MiddleName,
    string Status,
    bool EmailVerified,
    bool PhoneVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastLoginAt,
    UserProfileResponse? Profile,
    IReadOnlyList<UserAddressResponse> Addresses);

public sealed record UserStatusResponse(
    Guid Id,
    string Status);

public sealed record UserProfileResponse(
    DateOnly? BirthDate,
    string? Gender,
    string? AvatarUrl,
    string Language,
    bool MarketingEmailsEnabled);

public sealed record UserAddressResponse(
    Guid Id,
    string Country,
    string? Region,
    string City,
    string Street,
    string Building,
    string? Apartment,
    string? PostalCode,
    string RecipientName,
    string RecipientPhone,
    bool DefaultAddress);
