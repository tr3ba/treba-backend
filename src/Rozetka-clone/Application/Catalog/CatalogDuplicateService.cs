using Application.Abstractions;
using Contracts.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Application.Catalog;

public sealed class CatalogDuplicateService(IApplicationDbContext db)
{
    public static string Normalize(string? value) => string.Concat((value ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit));
    public async Task<IReadOnlyList<ProductDuplicateDto>> FindAsync(Guid? userId, Guid brandId, string? partNumber, string? barcode, Guid? excludeId, CancellationToken ct)
        => await FindManyAsync(userId, brandId, partNumber, string.IsNullOrWhiteSpace(barcode) ? [] : [barcode], excludeId, ct);
    public async Task<IReadOnlyList<ProductDuplicateDto>> FindManyAsync(Guid? userId, Guid brandId, string? partNumber, IReadOnlyList<string> barcodes, Guid? excludeId, CancellationToken ct)
    {
        if (barcodes is null || barcodes.Count > 100 || barcodes.Any(b => b is null || b.Length > 100)) throw new ArgumentException("Недопустимый список штрихкодов.");
        var part = Normalize(partNumber); var eans = barcodes.Select(Normalize).Where(b => b.Length > 0).Distinct().ToArray();
        if (part.Length == 0 && eans.Length == 0) return [];
        var query = db.Products.AsNoTracking().Where(p => p.Id != excludeId);
        if (userId.HasValue) query = query.Where(p => db.Stores.Any(s => s.Id == p.StoreId && db.Sellers.Any(v => v.Id == s.SellerId && v.UserId == userId)));
        query = query.Where(p => eans.Length > 0 && db.ProductVariants.Any(v => v.ProductId == p.Id && eans.Contains(v.Barcode!))
            || part.Length > 0 && p.BrandId == brandId && db.ProductIdentities.Any(i => i.ProductId == p.Id && i.NormalizedPartNumber == part));
        return await query.OrderByDescending(p => p.UpdatedAt).Take(10).Select(p => new ProductDuplicateDto(p.Id, p.Name,
            db.Stores.Where(s => s.Id == p.StoreId).Select(s => s.Name).FirstOrDefault() ?? "", "Совпадение штрихкода или бренда и артикула производителя")).ToListAsync(ct);
    }
}
