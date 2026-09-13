using Application.Abstractions;
using Domain.Entities.Product;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Products
{
    public sealed class ProductService : IProductService
    {
        private readonly IApplicationDbContext _dbContext;

        public ProductService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<ProductDto>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Products
                .AsNoTracking()
                .Where(x => x.Status == ProductStatus.ACTIVE)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => ToDto(x))
                .ToListAsync(cancellationToken);
        }

        public async Task<ProductDto?> GetBySlugAsync(
            string slug,
            CancellationToken cancellationToken = default)
        {
            var product = await _dbContext.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Slug == slug &&
                         x.Status == ProductStatus.ACTIVE,
                    cancellationToken);

            return product is null
                ? null
                : ToDto(product);
        }

        public async Task<ProductDto> CreateAsync(
            CreateProductRequest request,
            CancellationToken cancellationToken = default)
        {
            var slugExists = await _dbContext.Products
                .AnyAsync(
                    x => x.Slug == request.Slug,
                    cancellationToken);

            if (slugExists)
            {
                throw new InvalidOperationException(
                    $"Product with slug '{request.Slug}' already exists.");
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                StoreId = request.StoreId,
                CategoryId = request.CategoryId,
                BrandId = request.BrandId,

                Name = request.Name.Trim(),
                Slug = request.Slug.Trim().ToLowerInvariant(),

                ShortDescription = request.ShortDescription.Trim(),
                Description = request.Description.Trim(),

                Status = ProductStatus.DRAFT,

                AverageRating = 0,
                ReviewCount = 0,
                SalesCount = 0,

                WarrantyMonth = request.WarrantyMonth,
                CountryOfOrigin = request.CountryOfOrigin.Trim(),

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Products.Add(product);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(product);
        }

        public async Task<ProductDto?> UpdateAsync(
            Guid id,
            UpdateProductRequest request,
            CancellationToken cancellationToken = default)
        {
            var product = await _dbContext.Products
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);

            if (product is null)
                return null;

            if (request.CategoryId.HasValue)
                product.CategoryId = request.CategoryId.Value;

            if (request.BrandId.HasValue)
                product.BrandId = request.BrandId.Value;

            if (request.Name is not null)
                product.Name = request.Name.Trim();

            if (request.Slug is not null)
            {
                var slug = request.Slug.Trim().ToLowerInvariant();

                var exists = await _dbContext.Products
                    .AnyAsync(
                        x => x.Id != id && x.Slug == slug,
                        cancellationToken);

                if (exists)
                {
                    throw new InvalidOperationException(
                        $"Product with slug '{slug}' already exists.");
                }

                product.Slug = slug;
            }

            if (request.ShortDescription is not null)
                product.ShortDescription = request.ShortDescription.Trim();

            if (request.Description is not null)
                product.Description = request.Description.Trim();

            if (request.WarrantyMonth.HasValue)
                product.WarrantyMonth = request.WarrantyMonth.Value;

            if (request.CountryOfOrigin is not null)
                product.CountryOfOrigin = request.CountryOfOrigin.Trim();

            product.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(product);
        }

        public async Task<bool> SubmitForModerationAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var product = await FindProductAsync(id, cancellationToken);

            if (product is null)
                return false;

            if (product.Status != ProductStatus.DRAFT &&
                product.Status != ProductStatus.REJECTED)
            {
                throw new InvalidOperationException(
                    "Only draft or rejected product can be submitted for moderation.");
            }

            product.Status = ProductStatus.PENDING_MODERATION;
            product.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        public async Task<bool> ApproveAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var product = await FindProductAsync(id, cancellationToken);

            if (product is null)
                return false;

            if (product.Status != ProductStatus.PENDING_MODERATION)
            {
                throw new InvalidOperationException(
                    "Product must be pending moderation.");
            }

            product.Status = ProductStatus.ACTIVE;
            product.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        public async Task<bool> RejectAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var product = await FindProductAsync(id, cancellationToken);

            if (product is null)
                return false;

            if (product.Status != ProductStatus.PENDING_MODERATION)
            {
                throw new InvalidOperationException(
                    "Product must be pending moderation.");
            }

            product.Status = ProductStatus.REJECTED;
            product.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        private Task<Product?> FindProductAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            return _dbContext.Products
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        private static ProductDto ToDto(Product product)
        {
            return new ProductDto
            {
                Id = product.Id,
                StoreId = product.StoreId,
                CategoryId = product.CategoryId,
                BrandId = product.BrandId,

                Name = product.Name,
                Slug = product.Slug,

                ShortDescription = product.ShortDescription,
                Description = product.Description,

                Status = product.Status,

                AverageRating = product.AverageRating,
                ReviewCount = product.ReviewCount,
                SalesCount = product.SalesCount,

                WarrantyMonth = product.WarrantyMonth,
                CountryOfOrigin = product.CountryOfOrigin,

                CreatedAt = product.CreatedAt,
                UpdatedAt = product.UpdatedAt
            };
        }
    }
}
