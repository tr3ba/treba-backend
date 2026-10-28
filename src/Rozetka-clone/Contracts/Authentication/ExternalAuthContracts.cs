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
    string[] Scopes,
    string RedirectUri
);

public sealed record ExternalCodeLoginRequest(
    [Required] string Provider,
    [Required, MaxLength(8192)] string Code,
    [Required, MaxLength(2048)] string RedirectUri,
    [MaxLength(128)] string? CodeVerifier
);
