using Contracts.Catalog;
using Contracts.Marketplace;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Application.Catalog;

public sealed partial class CatalogService
{
    public async Task<PublicCatalogFacets> PublicFacetsAsync(Guid? categoryId, string? search, CancellationToken ct)
    {
        if (search?.Length > 200) throw new ArgumentException("Search is too long.");
        var products = db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.ACTIVE
            && db.Categories.Any(c => c.Id == p.CategoryId && c.IsActive) && db.Brands.Any(b => b.Id == p.BrandId && b.IsActive));
        if (categoryId.HasValue) { var ids = await CategoryBranchAsync(categoryId.Value, ct); products = products.Where(p => ids.Contains(p.CategoryId)); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            products = products.Where(p => p.Name.ToLower().Contains(term) || p.Slug.Contains(term)
                || db.ProductVariants.Any(v => v.ProductId == p.Id && v.Sku.ToLower().Contains(term))
                || db.Stores.Any(s => s.Id == p.StoreId && (s.Name.ToLower().Contains(term)
                    || db.Sellers.Any(v => v.Id == s.SellerId && v.CompanyName.ToLower().Contains(term)))));
        }
        var brands = await db.Brands.AsNoTracking().Where(b => b.IsActive && products.Any(p => p.BrandId == b.Id)).OrderBy(b => b.Name)
            .Select(b => new CatalogLookupDto(b.Id, b.Name)).ToListAsync(ct);
        var definitions = await db.Attributes.AsNoTracking().Where(a => a.IsFilterable && db.ProductAttributeValues.Any(v => v.AttributeId == a.Id && products.Any(p => p.Id == v.ProductId)))
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name).Take(30).ToListAsync(ct);
        var attributeIds = definitions.Select(a => a.Id).ToArray();
        var values = await db.ProductAttributeValues.AsNoTracking().Where(v => attributeIds.Contains(v.AttributeId) && products.Any(p => p.Id == v.ProductId))
            .Select(v => new { v.AttributeId, v.OptionId, v.StringValue, v.NumberValue, v.BoolValue }).Distinct().Take(5000).ToListAsync(ct);
        var options = await db.AttributeOptions.AsNoTracking().Where(o => attributeIds.Contains(o.AttributeId)).ToDictionaryAsync(o => o.Id, ct);
        var facets = definitions.Select(a => new CatalogFacet(a.Id, a.Name, values.Where(v => v.AttributeId == a.Id).Select(v =>
        {
            if (v.OptionId is { } id && options.TryGetValue(id, out var option)) return new CatalogFacetOption(option.Value, option.DisplayValue ?? option.Value);
            var text = v.StringValue ?? v.NumberValue?.ToString("G29", CultureInfo.InvariantCulture) ?? v.BoolValue?.ToString().ToLowerInvariant() ?? "";
            return new CatalogFacetOption(text, v.BoolValue.HasValue ? v.BoolValue.Value ? "Так" : "Ні" : text + (a.Unit is null ? "" : " " + a.Unit));
        }).Where(x => x.Value.Length is > 0 and <= 200).DistinctBy(x => x.Value).OrderBy(x => x.Label).Take(100).ToList())).ToList();
        return new(brands, facets);
    }
}
