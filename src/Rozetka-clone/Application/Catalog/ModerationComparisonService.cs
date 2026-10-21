using System.Globalization;
using System.Text.Json;
using Application.Abstractions;
using Contracts.Admin.Settings;
using Contracts.Catalog;
using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;

namespace Application.Catalog;

public sealed class ModerationComparisonService(IApplicationDbContext db)
{
    public async Task CaptureAsync(Product product, CancellationToken ct)
    {
        var baseline = await db.ProductReviewBaselines.SingleOrDefaultAsync(b => b.ProductId == product.Id, ct);
        if (baseline is null)
        {
            baseline = new()
            {
                ProductId = product.Id
            };
            db.ProductReviewBaselines.Add(baseline);
        }
        baseline.CapturedAt = DateTimeOffset.UtcNow;
        baseline.Snapshot = JsonSerializer.Serialize(await Snapshot(product, ct));
    }
    public async Task<ModerationComparisonDto> CompareAsync(Guid id, Guid? sellerUserId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id
            && (!sellerUserId.HasValue || db.Stores.Any(s => s.Id == p.StoreId && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == sellerUserId))), ct) ?? throw new KeyNotFoundException();
        var baseline = await db.ProductReviewBaselines.AsNoTracking().SingleOrDefaultAsync(b => b.ProductId == id, ct);
        if (baseline is null)
            return new(null, []);
        var before = JsonSerializer.Deserialize<Dictionary<string, string>>(baseline.Snapshot) ?? [];
        var after = await Snapshot(product, ct);
        return new(baseline.CapturedAt, before.Keys.Union(after.Keys).OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k))
            .Select(k => new AuditChangeDto(k, before.GetValueOrDefault(k), after.GetValueOrDefault(k))).ToList());
    }
    private async Task<Dictionary<string, string>> Snapshot(Product p, CancellationToken ct)
    {
        var category = await db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).SingleOrDefaultAsync(ct);
        var brand = await db.Brands.Where(b => b.Id == p.BrandId).Select(b => b.Name).SingleOrDefaultAsync(ct);
        var values = new Dictionary<string, string>
        {
            ["Name"] = p.Name,
            ["Slug"] = p.Slug,
            ["Description"] = p.Description,
            ["ShortDescription"] = p.ShortDescription,
            ["Category"] = category ?? p.CategoryId.ToString(),
            ["Brand"] = brand ?? p.BrandId.ToString(),
            ["Warranty"] = p.WarrantyMonths.ToString(CultureInfo.InvariantCulture),
            ["CountryOfOrigin"] = p.CountryOfOrigin
        };
        var variants = await db.ProductVariants.AsNoTracking().Where(v => v.ProductId == p.Id).OrderBy(v => v.Id).ToListAsync(ct);
        foreach (var v in variants)
        {
            var key = $"Variant / {v.Sku} / ";
            values[key + "Name"] = v.Name;
            values[key + "Price"] = v.Price.ToString(CultureInfo.InvariantCulture);
            values[key + "OldPrice"] = v.OldPrice?.ToString(CultureInfo.InvariantCulture) ?? "";
            values[key + "Stock"] = v.StockQuantity.ToString(CultureInfo.InvariantCulture);
            values[key + "Active"] = v.IsActive.ToString();
            values[key + "Barcode"] = v.Barcode ?? "";
        }
        var attributes = await (from a in db.ProductAttributeValues.AsNoTracking()
                                join definition in db.Attributes.AsNoTracking() on a.AttributeId equals definition.Id
                                where a.ProductId == p.Id
                                select new
                                {
                                    Value = a,
                                    definition.Name
                                }).ToListAsync(ct);
        var optionIds = attributes.Where(a => a.Value.OptionId.HasValue).Select(a => a.Value.OptionId!.Value).ToList();
        var options = await db.AttributeOptions.AsNoTracking().Where(o => optionIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Value, ct);
        foreach (var a in attributes.GroupBy(a => new { a.Value.AttributeId, a.Value.VariantId, a.Name }))
        {
            var variant = variants.FirstOrDefault(v => v.Id == a.Key.VariantId)?.Sku;
            values[$"Attribute / {a.Key.Name}" + (variant is null ? "" : " / " + variant)] = string.Join(", ", a.OrderBy(v => v.Value.OptionId).Select(v =>
                v.Value.StringValue ?? v.Value.NumberValue?.ToString(CultureInfo.InvariantCulture) ?? v.Value.BoolValue?.ToString() ?? (v.Value.OptionId.HasValue ? options.GetValueOrDefault(v.Value.OptionId.Value) : "") ?? ""));
        }
        var images = await db.ProductImages.AsNoTracking().Where(i => i.ProductId == p.Id).OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync(ct);
        values["Images"] = string.Join("\n", images.Select(i => $"{i.ImageUrl} · {i.AltText} · {i.IsMain}"));
        return values;
    }
}
