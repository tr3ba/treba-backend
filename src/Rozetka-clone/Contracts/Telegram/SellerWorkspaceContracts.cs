using System.ComponentModel.DataAnnotations;

namespace Contracts.Telegram;

public sealed class CreateSellerWorkspaceRequest : IValidatableObject
{
    [Required, EmailAddress, StringLength(320)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [Required, StringLength(250)] public string CompanyName { get; set; } = "";
    [StringLength(100)] public string? TaxNumber { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    [Required, StringLength(250)] public string StoreName { get; set; } = "";
    [Required, StringLength(100), RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")] public string StoreSlug { get; set; } = "";
    public bool TestSeller { get; set; }
    public bool Active { get; set; }
    public bool AllCategories { get; set; }
    [MaxLength(500)] public List<Guid> CategoryIds { get; set; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!TestSeller && string.IsNullOrWhiteSpace(TaxNumber)) yield return new("Укажите ЕГРПОУ/ИНН или включите тестовый режим.", [nameof(TaxNumber)]);
        if (!AllCategories && (CategoryIds is null || CategoryIds.Count == 0)) yield return new("Выберите разрешённые категории.", [nameof(CategoryIds)]);
    }
}
public sealed record SellerWorkspaceCreatedDto(Guid UserId, Guid SellerId, Guid StoreId, string Email, string Status);
public sealed record QueueImportsRequest([Required, MinLength(1), MaxLength(20)] List<ImportUrlRequest> Items);
public sealed record ImportJobDto(Guid Id, Guid BatchId, string SourceUrl, string Status, string? Error, Guid? PreviewId,
    Guid? ProductId, DateTimeOffset CreatedAt, int Attempts);
public sealed record ProductDuplicateDto(Guid Id, string Name, string StoreName, string Reason);
public sealed class DuplicateLookupRequest
{
    public Guid BrandId { get; set; }
    [StringLength(100)] public string ManufacturerPartNumber { get; set; } = "";
    [MaxLength(100)] public List<string> Barcodes { get; set; } = [];
    public Guid? ExcludeId { get; set; }
}
