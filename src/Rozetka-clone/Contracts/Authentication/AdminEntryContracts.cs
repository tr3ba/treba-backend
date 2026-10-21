using System.ComponentModel.DataAnnotations;

namespace Contracts.Authentication;

public sealed record AdminEntryResponse(string Code, DateTimeOffset ExpiresAt);
public sealed record RedeemAdminEntryRequest([Required, StringLength(100)] string Code);
