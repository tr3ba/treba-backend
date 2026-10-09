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

    public Guid CategoryId { get; set; }

    public Guid BrandId { get; set; }

    public Guid StoreId { get; set; }

    [StringLength(500)]
    public string ShortDescription { get; set; } = "";

    [StringLength(10000)]
    public string Description { get; set; } = "";

    [Required]
    [StringLength(100)]
    public string Sku { get; set; } = "";

    [Range(
        typeof(decimal),
        "0.01",
        "999999999.99",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true
    )]
    public decimal Price { get; set; }

    [Range(0, 1000000)]
    public int StockQuantity { get; set; }

    [Range(0, 120)]
    public int WarrantyMonths { get; set; }

    [StringLength(100)]
    public string CountryOfOrigin { get; set; } = "";

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

        if (decimal.Round(
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
);

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