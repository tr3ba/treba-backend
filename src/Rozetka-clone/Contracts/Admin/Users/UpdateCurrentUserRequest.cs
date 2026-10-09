using System.ComponentModel.DataAnnotations;

namespace Contracts.Admin.Users;

public sealed class UpdateCurrentUserRequest
{
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? MiddleName { get; set; }

    [Phone]
    [StringLength(32)]
    public string? Phone { get; set; }

    public DateOnly? BirthDate { get; set; }

    [StringLength(32)]
    public string? Gender { get; set; }

    [Required]
    [RegularExpression("^(uk|en|ru)$")]
    public string Language { get; set; } = "uk";

    public bool MarketingEmailsEnabled { get; set; }
}
