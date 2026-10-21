using System.ComponentModel.DataAnnotations;

namespace Contracts.Admin.Settings;

public sealed record StaffDto(Guid Id, string Email, string FirstName, string LastName, string Role, string Status, DateTimeOffset CreatedAt);

public sealed class CreateStaffRequest
{
    [Required, EmailAddress, MaxLength(320)] public string Email { get; set; } = "";
    [Required, MaxLength(100)] public string FirstName { get; set; } = "";
    [Required, MaxLength(100)] public string LastName { get; set; } = "";
    [Required, MinLength(12), MaxLength(128)] public string Password { get; set; } = "";
    [Required] public string Role { get; set; } = "Manager";
}

public sealed record UpdateStaffAccessRequest([Required] string Role, bool Blocked);
public sealed record AuditChangeDto(string Field, string? Before, string? After);
public sealed record AuditLogDto(Guid Id, DateTimeOffset OccurredAt, Guid ActorId, string ActorName, string ActorRole,
    string Action, string EntityType, Guid EntityId, string EntityName, IReadOnlyList<AuditChangeDto> Changes);
