using Application.Abstractions;
using Domain.Entities.Product;
using Domain.Enums;

namespace Application.Catalog;

public static class PublicCatalogVisibility
{
    public static IQueryable<Product> PubliclyVisible(this IQueryable<Product> products, IApplicationDbContext db) =>
        products.Where(p => p.Status == ProductStatus.ACTIVE
            && db.Categories.Any(c => c.Id == p.CategoryId && c.IsActive)
            && db.Brands.Any(b => b.Id == p.BrandId && b.IsActive)
            && db.Stores.Any(s => s.Id == p.StoreId && s.IsActive
                && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.Status == SellerStatus.ACTIVE)));
}
