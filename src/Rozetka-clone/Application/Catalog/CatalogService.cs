using Application.Abstractions;
using Application.Common;
using Contracts.Catalog;
using Contracts.Common;
using Domain.Entities.Product;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Catalog;

public sealed partial class CatalogService(
    IApplicationDbContext db
)
{
    public async Task<PagedResponse<CatalogProductDto>> ListAsync(
        int page,
        int size,
        string? search,
        string? status,
        bool publicOnly,
        Guid? categoryId,
        string? sort,
        CancellationToken ct,
        Guid? sellerUserId = null,
        CatalogFilters? filters = null)
    {
        if (page < 1 || page > 100000 || size is < 1 or > 100)
            throw new ArgumentException("Invalid pagination.");
        filters ??= new();
        if (filters.MinPrice < 0 || filters.MaxPrice < 0 || filters.MinPrice > filters.MaxPrice)
            throw new ArgumentException("Invalid price range.");
        if (!string.IsNullOrEmpty(filters.Stock) && filters.Stock is not ("available" or "low" or "out"))
            throw new ArgumentException("Unknown stock filter.");
        if (!string.IsNullOrEmpty(sort) && sort.ToLowerInvariant() is not ("priceasc" or "pricedesc" or "newest" or "oldest" or "nameasc" or "stockasc" or "updated" or "waiting"))
            throw new ArgumentException("Unknown catalog sort.");

        var query = db.Products.AsNoTracking();
        if (filters.WaitingHours.HasValue)
        {
            if (filters.WaitingHours is not (24 or 72))
                throw new ArgumentException("Waiting time must be 24 or 72 hours.");
            var cutoff = DateTime.UtcNow.AddHours(-filters.WaitingHours.Value);
            query = query.Where(p => p.Status == ProductStatus.PENDING_MODERATION && p.UpdatedAt <= cutoff);
        }

        if (sellerUserId.HasValue)
        {
            query = query.Where(
                p =>
                    db.Stores.Any(
                        store =>
                            store.Id == p.StoreId
                            && db.Sellers.Any(
                                seller =>
                                    seller.Id == store.SellerId
                                    && seller.UserId == sellerUserId.Value
                            )
                    )
            );
        }

        if (publicOnly)
        {
            query = query.Where(
                p =>
                    p.Status == ProductStatus.ACTIVE
                    && db.Categories.Any(
                        c =>
                            c.Id == p.CategoryId
                            && c.IsActive
                    )
                    && db.Brands.Any(
                        b =>
                            b.Id == p.BrandId
                            && b.IsActive
                    )
            );
        }
        else if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ProductStatus>(
                    status,
                    out var parsed
                )
                || !Enum.IsDefined(parsed))
            {
                throw new ArgumentException("Unknown product status.");
            }

            query = query.Where(p => p.Status == parsed);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search
                .Trim()
                .ToLowerInvariant();

            query = query.Where(
                p =>
                    p.Name
                        .ToLower()
                        .Contains(term)
                    || p.Slug.Contains(term)
                    || db.Stores.Any(store => store.Id == p.StoreId && (store.Name.ToLower().Contains(term)
                        || db.Sellers.Any(seller => seller.Id == store.SellerId && seller.CompanyName.ToLower().Contains(term))))
                    || db.ProductVariants.Any(
                        v =>
                            v.ProductId == p.Id
                            && v.Sku
                                .ToLower()
                                .Contains(term)
                    )
            );
        }

        if (categoryId.HasValue)
        {
            var categoryIds = await CategoryBranchAsync(categoryId.Value, ct);
            query = query.Where(p => categoryIds.Contains(p.CategoryId));
        }

        if (filters.BrandId.HasValue)
            query = query.Where(p => p.BrandId == filters.BrandId.Value);
        if (filters.SellerId.HasValue)
            query = query.Where(p => db.Stores.Any(s => s.Id == p.StoreId && s.SellerId == filters.SellerId.Value));
        if (filters.MinPrice.HasValue || filters.MaxPrice.HasValue)
            query = query.Where(p => db.ProductVariants.Any(v => v.ProductId == p.Id && v.IsActive
                && (!filters.MinPrice.HasValue || v.Price >= filters.MinPrice.Value)
                && (!filters.MaxPrice.HasValue || v.Price <= filters.MaxPrice.Value)));
        if (filters.Stock == "available")
            query = query.Where(p => db.ProductVariants.Any(v => v.ProductId == p.Id && v.IsActive && v.StockQuantity > 0));
        if (filters.Stock == "out")
            query = query.Where(p => !db.ProductVariants.Any(v => v.ProductId == p.Id && v.IsActive && v.StockQuantity > 0));
        if (filters.Stock == "low")
            query = query.Where(p => db.ProductVariants.Where(v => v.ProductId == p.Id && v.IsActive).Sum(v => v.StockQuantity) > 0
            && db.ProductVariants.Where(v => v.ProductId == p.Id && v.IsActive).Sum(v => v.StockQuantity) <= 5);

        var count = await query.CountAsync(ct);

        query = sort?.Trim().ToLowerInvariant() switch
        {
            "nameasc" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            "oldest" => query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id),
            "waiting" => query.OrderBy(p => p.UpdatedAt).ThenBy(p => p.Id),
            "updated" => query.OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Id),
            "stockasc" => query.OrderBy(p => db.ProductVariants.Where(v => v.ProductId == p.Id && v.IsActive).Sum(v => v.StockQuantity)).ThenBy(p => p.Id),
            "priceasc" => query
                .OrderBy(p => db.ProductVariants
                    .Where(v => v.ProductId == p.Id && v.IsActive)
                    .Select(v => (decimal?)v.Price)
                    .Min() ?? decimal.MaxValue)
                .ThenBy(p => p.Id),

            "pricedesc" => query
                .OrderByDescending(p => db.ProductVariants
                    .Where(v => v.ProductId == p.Id && v.IsActive)
                    .Select(v => (decimal?)v.Price)
                    .Min() ?? 0)
                .ThenBy(p => p.Id),

            _ => query
                .OrderByDescending(p => p.CreatedAt)
                .ThenBy(p => p.Id)
        };

        var products = await query
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new(
            await MapAsync(products, ct),
            page,
            size,
            count,
            (int)Math.Ceiling(count / (double)size)
        );
    }

    public async Task<CatalogProductDto?> GetAsync(
        Guid id,
        CancellationToken ct,
        Guid? sellerUserId = null)
    {
        var product = await db
            .Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p =>
                    p.Id == id
                    && (
                        !sellerUserId.HasValue
                        || db.Stores.Any(
                            store =>
                                store.Id == p.StoreId
                                && db.Sellers.Any(
                                    seller =>
                                        seller.Id == store.SellerId
                                        && seller.UserId == sellerUserId.Value
                                )
                        )
                    ),
                ct
            );

        return product is null
            ? null
            : (await MapAsync([product], ct))[0];
    }

    public async Task<CatalogProductDto?> GetBySlugAsync(
        string slug,
        bool publicOnly,
        CancellationToken ct)
    {
        var query = db.Products
            .AsNoTracking()
            .Where(p => p.Slug == slug);

        if (publicOnly)
        {
            query = query.Where(
                p =>
                    p.Status == ProductStatus.ACTIVE
                    && db.Categories.Any(
                        c =>
                            c.Id == p.CategoryId
                            && c.IsActive
                    )
                    && db.Brands.Any(
                        b =>
                            b.Id == p.BrandId
                            && b.IsActive
                    )
            );
        }

        var product = await query.FirstOrDefaultAsync(ct);

        return product is null
            ? null
            : (await MapAsync([product], ct))[0];
    }

    public async Task<CatalogStatsDto> StatsAsync(
        CancellationToken ct,
        Guid? sellerUserId = null)
    {
        var query = db.Products.AsNoTracking();

        if (sellerUserId.HasValue)
        {
            query = query.Where(
                p =>
                    db.Stores.Any(
                        store =>
                            store.Id == p.StoreId
                            && db.Sellers.Any(
                                seller =>
                                    seller.Id == store.SellerId
                                    && seller.UserId == sellerUserId.Value
                            )
                    )
            );
        }

        return new(
            await query.CountAsync(ct),
            await query.CountAsync(
                p => p.Status == ProductStatus.ACTIVE,
                ct
            ),
            await query.CountAsync(
                p => p.Status == ProductStatus.PENDING_MODERATION,
                ct
            ),
            await query.CountAsync(
                p => p.Status == ProductStatus.DRAFT,
                ct
            ),
            await query.CountAsync(
                p => p.Status == ProductStatus.REJECTED,
                ct
            ),
            await query.CountAsync(
                p =>
                    !db.ProductVariants.Any(
                        v =>
                            v.ProductId == p.Id
                            && v.IsActive
                            && v.StockQuantity > 0
                    ),
                ct
            )
        );
    }

    public Task<int> PendingCountAsync(
        CancellationToken ct)
    {
        return db.Products.CountAsync(
            product => product.Status == ProductStatus.PENDING_MODERATION,
            ct
        );
    }

    public Task<CatalogProductDto?> SaveAsync(Guid? id, Guid storeId, SaveCatalogProductRequest request, CancellationToken ct)
        => SaveProductAsync(id, storeId, request, ct);

    private async Task<List<CatalogProductDto>> MapAsync(
        List<Product> products,
        CancellationToken ct)
    {
        var ids = products
            .Select(p => p.Id)
            .ToArray();

        var categoryIds = products
            .Select(p => p.CategoryId)
            .Distinct()
            .ToArray();

        var brandIds = products
            .Select(p => p.BrandId)
            .Distinct()
            .ToArray();

        var categories = await db
            .Categories
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(
                c => c.Id,
                c => c.Name,
                ct
            );

        var brands = await db
            .Brands
            .Where(b => brandIds.Contains(b.Id))
            .ToDictionaryAsync(
                b => b.Id,
                b => b.Name,
                ct
            );

        var variants = await db
            .ProductVariants
            .AsNoTracking()
            .Where(v => ids.Contains(v.ProductId))
            .OrderBy(v => v.Sku)
            .ToListAsync(ct);

        var identities = await db.ProductIdentities.AsNoTracking().Where(i => ids.Contains(i.ProductId)).ToDictionaryAsync(i => i.ProductId, i => i.ManufacturerPartNumber, ct);
        var images = await db
            .ProductImages
            .AsNoTracking()
            .Where(image => ids.Contains(image.ProductId))
            .OrderByDescending(image => image.IsMain)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .ToListAsync(ct);

        var storeIds = products.Select(p => p.StoreId).Distinct().ToArray();
        var stores = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var sellerIds = stores.Values.Select(s => s.SellerId).ToArray();
        var sellers = await db.Sellers.AsNoTracking().Where(s => sellerIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.CompanyName, ct);
        var values = await db.ProductAttributeValues.AsNoTracking().Where(v => ids.Contains(v.ProductId)).ToListAsync(ct);

        return products
            .Select(
                p =>
                {
                    var productVariants = variants.Where(v => v.ProductId == p.Id).ToList();
                    var variant = productVariants.FirstOrDefault(v => v.IsActive) ?? productVariants.FirstOrDefault();

                    var productImages = images
                        .Where(image => image.ProductId == p.Id)
                        .Select(
                            image =>
                                new CatalogProductImageDto(
                                    image.Id,
                                    image.ProductId,
                                    image.VariantId,
                                    image.ImageUrl,
                                    image.AltText,
                                    image.SortOrder,
                                    image.IsMain
                                )
                        )
                        .ToList();

                    return new CatalogProductDto(
                        p.Id,
                        p.StoreId,
                        p.CategoryId,
                        p.BrandId,
                        variant?.Id,
                        p.Name,
                        p.Slug,
                        categories.GetValueOrDefault(
                            p.CategoryId,
                            "—"
                        ),
                        brands.GetValueOrDefault(
                            p.BrandId,
                            "—"
                        ),
                        p.Status.ToString(),
                        p.ShortDescription,
                        p.Description,
                        variant?.Sku ?? "",
                        productVariants.Where(v => v.IsActive).Select(v => (decimal?)v.Price).Min() ?? variant?.Price ?? 0,
                        variant?.OldPrice,
                        productVariants.Where(v => v.IsActive).Sum(v => v.StockQuantity),
                        p.WarrantyMonths,
                        p.CountryOfOrigin,
                        p.CreatedAt,
                        productImages
                    )
                    {
                        StoreName = stores.GetValueOrDefault(p.StoreId)?.Name ?? "",
                        SellerName = stores.TryGetValue(p.StoreId, out var store) ? sellers.GetValueOrDefault(store.SellerId, "") : "",
                        UpdatedAt = p.UpdatedAt,
                        ManufacturerPartNumber = identities.GetValueOrDefault(p.Id, ""),
                        Variants = productVariants.Select(v => new CatalogVariantInput { Id = v.Id, Name = v.Name, Sku = v.Sku, Barcode = v.Barcode, Price = v.Price, OldPrice = v.OldPrice, StockQuantity = v.StockQuantity, IsActive = v.IsActive }).ToList(),
                        Attributes = values.Where(v => v.ProductId == p.Id).Select(v => new CatalogAttributeInput { AttributeId = v.AttributeId, VariantId = v.VariantId, OptionId = v.OptionId, StringValue = v.StringValue, NumberValue = v.NumberValue, BoolValue = v.BoolValue }).ToList()
                    };
                }
            )
            .ToList();
    }
}
