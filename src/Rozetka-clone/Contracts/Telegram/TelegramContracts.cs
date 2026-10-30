using System.ComponentModel.DataAnnotations;
using Contracts.Catalog;

namespace Contracts.Telegram;

public sealed record TelegramConfigurationDto(bool Configured, string? BotUsername, string? MiniAppUrl);
public sealed record TelegramLinkDto(bool Configured, bool Linked, string? Username, long? TelegramUserId,
    DateTimeOffset? LinkedAt, Guid? PendingId, string? PendingTelegramName, string? PendingUsername);
public sealed record TelegramLinkStartDto(Guid Id, string BotUrl, DateTimeOffset ExpiresAt);
public sealed record ConfirmTelegramLinkRequest(Guid ChallengeId);
public sealed record TelegramUserDto(Guid Id, string Name, string Role, long TelegramId);

public sealed record CreateGuardRequest([StringLength(200)] string? DeviceName, Guid? LoginChallengeId = null);
public sealed record GuardStartDto(Guid Id, string BrowserSecret, string QrPayload, string QrCodeDataUrl, string DisplayCode, DateTimeOffset ExpiresAt);
public sealed record GuardBrowserRequest(Guid Id, [Required, StringLength(64)] string BrowserSecret);
public sealed record GuardScanRequest([Required, StringLength(200)] string QrPayload);
public sealed record GuardPreviewDto(Guid Id, string DeviceName, string DisplayCode, DateTimeOffset ExpiresAt);
public sealed record GuardStatusDto(string Status, DateTimeOffset ExpiresAt);

public sealed record SellerCategoryGrantInput(Guid CategoryId, bool IncludeDescendants = true);
public sealed record UpdateSellerCategoriesRequest([Required, MaxLength(500)] List<SellerCategoryGrantInput> Grants);
public sealed record SellerCategoryGrantDto(Guid CategoryId, string Name, bool IncludeDescendants);
public sealed record CategoryMappingInput([Required, RegularExpression(@"^(?:c[1-9][0-9]{0,11}|(?:prom|epic|web):[a-f0-9]{12})$")] string ExternalKey, Guid CategoryId);
public sealed record CategoryMappingDto(Guid Id, string ExternalKey, Guid CategoryId, string CategoryName);

public sealed record ImportUrlRequest([Required, StringLength(1500)] string Url, Guid? CategoryId = null);
public sealed record ImportedCharacteristicDto(string Name, string Value);
public sealed record ImportPreviewDto(Guid Id, string SourceUrl, string Name, string Description, string BrandName,
    string SourceCategoryName, Guid CategoryId, string CategoryName, decimal? SourcePrice,
    IReadOnlyList<string> Images, IReadOnlyList<ImportedCharacteristicDto> Characteristics, DateTimeOffset ExpiresAt,
    Guid? ProductId, DateTimeOffset? SubmittedAt)
{
    public Guid? BrandId { get; init; }
    public string ManufacturerPartNumber { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string SourceSku { get; init; } = "";
    public string SourceCurrency { get; init; } = "";
    public IReadOnlyList<string> SourceWarnings { get; init; } = [];
    public IReadOnlyList<ProductDuplicateDto> Duplicates { get; init; } = [];
    public bool CanClassifyCategory { get; init; }
}
public sealed record CreateImportBrandRequest([Required, StringLength(200)] string Name);
public sealed record CreateImportCategoryRequest([Required, StringLength(200)] string Name, Guid ParentId);
public sealed record SelectImportCategoryRequest(Guid CategoryId);
public sealed record SellerImportOptionsDto(IReadOnlyList<CatalogLookupDto> Categories, IReadOnlyList<CatalogLookupDto> Brands,
    IReadOnlyList<CatalogLookupDto> Stores, bool CanClassifyImports);
public sealed class SubmitImportRequest
{
    [Required, StringLength(250)] public string Name { get; set; } = "";
    [StringLength(10000)] public string Description { get; set; } = "";
    public Guid StoreId { get; set; }
    public Guid BrandId { get; set; }
    [StringLength(100)] public string ManufacturerPartNumber { get; set; } = "";
    [StringLength(100)] public string Barcode { get; set; } = "";
    public bool DuplicateConfirmed { get; set; }
    [Required, StringLength(100)] public string Sku { get; set; } = "";
    [Range(typeof(decimal), "0.01", "999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Price { get; set; }
    [Range(0, 1000000)] public int StockQuantity { get; set; }
    public List<CatalogAttributeInput> Attributes { get; set; } = [];
}
