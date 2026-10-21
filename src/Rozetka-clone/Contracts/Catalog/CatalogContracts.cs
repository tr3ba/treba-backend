using System.ComponentModel.DataAnnotations;

namespace Contracts.Catalog;

public sealed class SaveCatalogProductRequest : IValidatableObject
{
    [Required]
    [StringLength(250)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(300)]
    [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    public string Slug { get; set; } = "";

    public Guid CategoryId
    {
        get; set;
    }

    public Guid BrandId
    {
        get; set;
    }

    public Guid StoreId
    {
        get; set;
    }

    [StringLength(500)]
    public string ShortDescription { get; set; } = "";

    [StringLength(10000)]
    public string Description { get; set; } = "";

    [StringLength(100)]
    public string Sku { get; set; } = "";

    public decimal Price
    {
        get; set;
    }

    [Range(0, 1000000)]
    public int StockQuantity
    {
        get; set;
    }

    [Range(0, 120)]
    public int WarrantyMonths
    {
        get; set;
    }

    [StringLength(100)]
    public string CountryOfOrigin { get; set; } = "";

    public DateTime? ExpectedUpdatedAt
    {
        get; set;
    }
    [StringLength(100)] public string ManufacturerPartNumber { get; set; } = "";
    public List<CatalogVariantInput>? Variants
    {
        get; set;
    }
    public List<CatalogAttributeInput>? Attributes
    {
        get; set;
    }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext
    )
    {
        if (CategoryId == Guid.Empty)
        {
            yield return new(
                "Select a category.",
                [nameof(CategoryId)]
            );
        }

        if (BrandId == Guid.Empty)
        {
            yield return new(
                "Select a brand.",
                [nameof(BrandId)]
            );
        }

        if (Variants is null && (string.IsNullOrWhiteSpace(Sku) || Price is < 0.01m or > 999999999.99m))
            yield return new("SKU and a positive price are required.", [nameof(Sku), nameof(Price)]);
        if (Variants is not null)
        {
            if (Variants.Count is < 1 or > 100 || !Variants.Any(v => v is not null && v.IsActive))
                yield return new("Add between 1 and 100 variants, including an active variant.", [nameof(Variants)]);
            if (Variants.Any(v => v is null))
                yield return new("A variant cannot be null.", [nameof(Variants)]);
            foreach (var variant in Variants.Where(v => v is not null))
            {
                var errors = new List<ValidationResult>();
                Validator.TryValidateObject(variant, new(variant), errors, true);
                foreach (var error in errors)
                    yield return new(error.ErrorMessage, [nameof(Variants)]);
            }
        }
        if (Attributes?.Count > 200)
            yield return new("At most 200 characteristic values are allowed.", [nameof(Attributes)]);
        if (Attributes?.Any(a => a is null) == true)
            yield return new("A characteristic cannot be null.", [nameof(Attributes)]);
        if (Variants is null && decimal.Round(
                Price,
                2
            ) != Price)
        {
            yield return new(
                "Price can have at most two decimal places.",
                [nameof(Price)]
            );
        }
    }
}

public sealed record CatalogProductDto(
    Guid Id,
    Guid StoreId,
    Guid CategoryId,
    Guid BrandId,
    Guid? ProductVariantId,
    string Name,
    string Slug,
    string CategoryName,
    string BrandName,
    string Status,
    string ShortDescription,
    string Description,
    string Sku,
    decimal Price,
    decimal? OldPrice,
    int StockQuantity,
    int WarrantyMonths,
    string CountryOfOrigin,
    DateTime CreatedAt,
    IReadOnlyList<CatalogProductImageDto> Images
)
{
    public string StoreName { get; init; } = "";
    public string SellerName { get; init; } = "";
    public DateTime UpdatedAt
    {
        get; init;
    }
    public string ManufacturerPartNumber { get; init; } = "";
    public IReadOnlyList<CatalogVariantInput> Variants { get; init; } = [];
    public IReadOnlyList<CatalogAttributeInput> Attributes { get; init; } = [];
}

public sealed record CatalogLookupDto(
    Guid Id,
    string Name
);

public sealed record CatalogStatsDto(
    int Total,
    int Active,
    int Pending,
    int Drafts,
    int Rejected,
    int OutOfStock
);

public sealed record CatalogProductImageDto(
    Guid Id,
    Guid ProductId,
    Guid? VariantId,
    string ImageUrl,
    string? AltText,
    int SortOrder,
    bool IsMain
);

public sealed class CatalogVariantInput : IValidatableObject
{
    [StringLength(100)]
    public string? Barcode
    {
        get; set;
    }
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(100)] public string Sku { get; set; } = "";
    [Required, StringLength(250)] public string Name { get; set; } = "";
    [Range(typeof(decimal), "0.01", "999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Price
    {
        get; set;
    }
    public decimal? OldPrice
    {
        get; set;
    }
    [Range(0, 1000000)]
    public int StockQuantity
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Sku) || string.IsNullOrWhiteSpace(Name))
            yield return new("Variant name, SKU and id are required.");
        if (decimal.Round(Price, 2) != Price || OldPrice is { } old && (old <= Price || old > 999999999.99m || decimal.Round(old, 2) != old))
            yield return new("Prices must have at most two decimals; the old price must exceed the current price.");
    }
}

public sealed class CatalogAttributeInput
{
    public Guid AttributeId
    {
        get; set;
    }
    public Guid? VariantId
    {
        get; set;
    }
    public Guid? OptionId
    {
        get; set;
    }
    public string? StringValue
    {
        get; set;
    }
    public decimal? NumberValue
    {
        get; set;
    }
    public bool? BoolValue
    {
        get; set;
    }
}

public sealed class CatalogFilters
{
    public Guid? BrandId
    {
        get; set;
    }
    public Guid? SellerId
    {
        get; set;
    }
    public decimal? MinPrice
    {
        get; set;
    }
    public decimal? MaxPrice
    {
        get; set;
    }
    public string? Stock
    {
        get; set;
    }
    public int? WaitingHours
    {
        get; set;
    }
}
public sealed record CatalogFilterOptionsDto(IReadOnlyList<CatalogLookupDto> Categories, IReadOnlyList<CatalogLookupDto> Brands, IReadOnlyList<CatalogLookupDto> Sellers);
public sealed record CatalogAttributeDefinitionDto(Guid Id, string Name, string Type, string? Unit, bool IsRequired, IReadOnlyList<CatalogLookupDto> Options);
public sealed record ProductModerationDto(Guid Id, DateTimeOffset OccurredAt, string ActorName, string Decision, string? Reason);
public sealed record RejectProductRequest([Required, StringLength(2000)] string Reason);
public sealed record ModerationComparisonDto(DateTimeOffset? CapturedAt, IReadOnlyList<Contracts.Admin.Settings.AuditChangeDto> Changes);
public sealed record CreateCatalogAttributeRequest(Guid CategoryId, [Required, StringLength(200)] string Name, [StringLength(30)] string? Unit);
