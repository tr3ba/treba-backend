using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Catalog;

public sealed partial class CatalogService
{
    // Preserve variant ids and product snapshots referenced by existing orders.
    public async Task<bool> DeleteAsync(Guid id, Guid? sellerUserId, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id && (!sellerUserId.HasValue
            || db.Stores.Any(s => s.Id == p.StoreId && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == sellerUserId.Value))), ct);
        if (product is null) return false;
        if (product.Status == ProductStatus.ARCHIVED) return true;
        product.Status = ProductStatus.ARCHIVED;
        product.UpdatedAt = DateTime.UtcNow;
        foreach (var variant in await db.ProductVariants.Where(v => v.ProductId == id && v.IsActive).ToListAsync(ct))
            variant.Update(variant.Sku, variant.Barcode, variant.Name, variant.Price, variant.OldPrice, variant.CostPrice,
                variant.Weight, variant.Length, variant.Width, variant.Height, false);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
