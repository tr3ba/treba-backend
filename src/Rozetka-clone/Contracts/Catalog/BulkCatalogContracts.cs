using System.ComponentModel.DataAnnotations;

namespace Contracts.Catalog;

public sealed class BulkCatalogRequest : IValidatableObject
{
    [Required, MinLength(1), MaxLength(100)] public List<Guid> ProductIds { get; set; } = [];
    [Range(typeof(decimal), "0.01", "999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? Price { get; set; }
    [Range(typeof(decimal), "-99.99", "1000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? PricePercent { get; set; }
    [Range(0, 1000000)] public int? StockQuantity { get; set; }
    public bool? IsActive { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Price.HasValue && PricePercent.HasValue) yield return new("Виберіть фіксовану ціну або зміну у відсотках.");
        if (!Price.HasValue && !PricePercent.HasValue && !StockQuantity.HasValue && !IsActive.HasValue) yield return new("Укажіть хоча б одну зміну.");
        if (ProductIds is not null && (ProductIds.Any(id => id == Guid.Empty) || ProductIds.Distinct().Count() != ProductIds.Count)) yield return new("Кожен товар можна вибрати лише один раз.");
    }
}
public sealed record BulkCatalogRowDto(Guid ProductId, Guid VariantId, string Name, string Sku, decimal BeforePrice, decimal AfterPrice,
    int BeforeStock, int AfterStock, bool BeforeActive, bool AfterActive)
{
    public decimal? BeforeOldPrice { get; init; }
    public decimal? AfterOldPrice { get; init; }
}
public sealed record BulkCatalogPreviewDto(string Token, DateTimeOffset ExpiresAt, IReadOnlyList<BulkCatalogRowDto> Rows);
public sealed record ApplyBulkCatalogRequest([Required, StringLength(1000000)] string Token);
