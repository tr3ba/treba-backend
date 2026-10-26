using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public sealed record ExternalLoginRequest(
    [Required] string Provider,
    [Required] string AccessToken
);

public sealed record ExternalLoginResponse(
    bool Success,
    string? Message,
    AuthResponse? AuthResponse
);

public sealed record ExternalProviderInfo(
    string Name,
    string ClientId,
    string AuthorizationUrl,
    string[] Scopes
);