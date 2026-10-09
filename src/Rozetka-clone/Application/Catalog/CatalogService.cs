using Application.Abstractions;
using Application.Common;
using Contracts.Catalog;
using Contracts.Common;
using Domain.Entities.Product;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Catalog;

public sealed class CatalogService(
    IApplicationDbContext db
)
{
    public async Task<PagedResponse<CatalogProductDto>> ListAsync(
        int page,
        int size,
        string? search,
        string? status,
        bool publicOnly,
        CancellationToken ct,
        Guid? sellerUserId = null
    )
    {
        page = Math.Max(
            1,
            page
        );
        size = Math.Clamp(
            size,
            1,
            100
        );
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
                    || db.ProductVariants.Any(
                        v =>
                            v.ProductId == p.Id
                            && v.Sku
                                .ToLower()
                                .Contains(term)
                    )
            );
        }
        var count = await query.CountAsync(ct);
        var products = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        return new(
            await MapAsync(
                products,
                ct
            ),
            page,
            size,
            count,
            (int)Math.Ceiling(count / (double)size)
        );
    }

    public async Task<CatalogProductDto?> GetAsync(
        Guid id,
        CancellationToken ct,
        Guid? sellerUserId = null
    )
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
            : (
                await MapAsync(
                    [product],
                    ct
                )
            )[0];
    }

    public async Task<CatalogStatsDto> StatsAsync(
        CancellationToken ct,
        Guid? sellerUserId = null
    )
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
        CancellationToken ct
    )
    {
        return db.Products.CountAsync(
            product => product.Status == ProductStatus.PENDING_MODERATION,
            ct
        );
    }

    public async Task<CatalogProductDto?> SaveAsync(
        Guid? id,
        Guid storeId,
        SaveCatalogProductRequest request,
        CancellationToken ct
    )
    {
        var validation = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request,
            new(request),
            validation,
            true
        ))
        {
            throw new ArgumentException(
                string.Join(
                    " ",
                    validation.Select(v => v.ErrorMessage)
                )
            );
        }

        var slug = request.Slug
            .Trim()
            .ToLowerInvariant();
        var sku = request.Sku
            .Trim()
            .ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("Name and SKU are required.");
        }

        if (!await db.Categories.AnyAsync(
            c =>
                c.Id == request.CategoryId
                && c.IsActive,
            ct
        ))
        {
            throw new ArgumentException("Select an active category.");
        }

        if (!await db.Brands.AnyAsync(
            b =>
                b.Id == request.BrandId
                && b.IsActive,
            ct
        ))
        {
            throw new ArgumentException("Select an active brand.");
        }

        if (await db.Products.AnyAsync(
            p =>
                p.Slug == slug
                && (
                    !id.HasValue
                    || p.Id != id.Value
                ),
            ct
        ))
        {
            throw new BusinessRuleException("Product slug is already in use.");
        }

        if (
            await db.ProductVariants.AnyAsync(
                v =>
                    v.Sku.ToUpper() == sku
                    && (
                        !id.HasValue
                        || v.ProductId != id.Value
                    ),
                ct
            )
        )
        {
            throw new BusinessRuleException("SKU is already in use.");
        }

        var product = id.HasValue
            ? await db.Products.FirstOrDefaultAsync(
                p => p.Id == id.Value,
                ct
            )
            : new Product
            {
                Id = Guid.NewGuid(),
                StoreId = storeId,
                CreatedAt = DateTime.UtcNow,
            };
        if (product is null)
        {
            return null;
        }

        if (product.Status == ProductStatus.PENDING_MODERATION
            || product.Status == ProductStatus.ARCHIVED)
        {
            throw new BusinessRuleException("A product under review or archived cannot be edited.");
        }

        var variant = id.HasValue
            ? await db.ProductVariants
                .OrderBy(v => v.Sku)
                .FirstOrDefaultAsync(
                    v => v.ProductId == product.Id,
                    ct
                )
            : null;
        product.Name = request.Name.Trim();
        product.Slug = slug;
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.ShortDescription = request.ShortDescription.Trim();
        product.Description = request.Description.Trim();
        product.WarrantyMonths = request.WarrantyMonths;
        product.CountryOfOrigin = request.CountryOfOrigin.Trim();
        product.Status = ProductStatus.DRAFT;
        product.UpdatedAt = DateTime.UtcNow;
        if (!id.HasValue)
        {
            db.Products.Add(product);
        }

        if (variant is null)
        {
            variant = new ProductVariant(
                Guid.NewGuid(),
                product.Id,
                sku,
                null,
                "Основний",
                request.Price,
                null,
                null,
                null,
                null,
                null,
                null,
                true
            );
            db.ProductVariants.Add(variant);
        }
        else
        {
            variant.Update(
                sku,
                variant.Barcode,
                variant.Name,
                request.Price,
                variant.OldPrice,
                variant.CostPrice,
                variant.Weight,
                variant.Length,
                variant.Width,
                variant.Height,
                variant.IsActive
            );
        }

        variant.SetStock(request.StockQuantity);
        // Product and variant are committed together by EF's SaveChanges transaction.
        await db.SaveChangesAsync(ct);
        return await GetAsync(
            product.Id,
            ct
        );
    }

    private async Task<List<CatalogProductDto>> MapAsync(
        List<Product> products,
        CancellationToken ct
    )
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
        var brands = await db.Brands
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
        var images = await db
            .ProductImages
            .AsNoTracking()
            .Where(image => ids.Contains(image.ProductId))
            .OrderByDescending(image => image.IsMain)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .ToListAsync(ct);
        return products
            .Select(
                p =>
                {
                    var variant = variants.FirstOrDefault(v => v.ProductId == p.Id);
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
                        variant?.Price ?? 0,
                        variant?.StockQuantity ?? 0,
                        p.WarrantyMonths,
                        p.CountryOfOrigin,
                        p.CreatedAt,
                        productImages
                    );
                }
            )
            .ToList();
    }
}
