using System.ComponentModel.DataAnnotations;

namespace Contracts.Catalog;

public sealed class BulkCatalogRequest : IValidatableObject
{
    [Required, MinLength(1), MaxLength(100)] public List<Guid> ProductIds { get; set; } = [];
    [Range(typeof(decimal), "0.01", "999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? Price
    {
        get; set;
    }

    [Range(typeof(decimal), "-99.99", "1000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? PricePercent
    {
        get; set;
    }

    [Range(0, 1000000000000)]
    public long? StockQuantity
    {
        get; set;
    }

    public bool? IsActive
    {
        get; set;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var hasMultiplePriceChanges = Price.HasValue && PricePercent.HasValue;

        if (hasMultiplePriceChanges)
            yield return new("Виберіть фіксовану ціну або зміну у відсотках.");

        var hasNoChanges = !Price.HasValue
            && !PricePercent.HasValue
            && !StockQuantity.HasValue
            && !IsActive.HasValue;

        if (hasNoChanges)
            yield return new("Укажіть хоча б одну зміну.");

        var containsInvalidProductId = ProductIds is not null
            && ProductIds.Any(id => id == Guid.Empty);
        var containsDuplicateProductId = ProductIds is not null
            && ProductIds.Distinct().Count() != ProductIds.Count;

        if (containsInvalidProductId || containsDuplicateProductId)
            yield return new("Кожен товар можна вибрати лише один раз.");
    }
}
public sealed record BulkCatalogRowDto(Guid ProductId, Guid VariantId, string Name, string Sku, decimal BeforePrice, decimal AfterPrice,
    long BeforeStock, long AfterStock, bool BeforeActive, bool AfterActive)
{
    public decimal? BeforeOldPrice
    {
        get; init;
    }

    public decimal? AfterOldPrice
    {
        get; init;
    }
}
public sealed record BulkCatalogPreviewDto(string Token, DateTimeOffset ExpiresAt, IReadOnlyList<BulkCatalogRowDto> Rows);
public sealed record ApplyBulkCatalogRequest([Required, StringLength(1000000)] string Token);
