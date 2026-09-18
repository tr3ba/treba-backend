using Application.Abstractions;
using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductVariants
{
    public sealed class ProductVariantService : IProductVariantService
    {
        private readonly IApplicationDbContext _dbContext;

        public ProductVariantService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<ProductVariantDto>> GetByProductIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ProductVariants
                .AsNoTracking()
                .Where(x => x.ProductId == productId)
                .OrderBy(x => x.Name)
                .Select(x => new ProductVariantDto
                {
                    Id = x.Id,
                    ProductId = x.ProductId,
                    Sku = x.Sku,
                    Barcode = x.Barcode,
                    Name = x.Name,
                    Price = x.Price,
                    OldPrice = x.OldPrice,
                    Weight = x.Weight,
                    Length = x.Length,
                    Width = x.Width,
                    Height = x.Height,
                    IsActive = x.IsActive
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<ProductVariantDto?> GetByIdAsync(
            Guid productId,
            Guid variantId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ProductVariants
                .AsNoTracking()
                .Where(x => x.Id == variantId &&
                            x.ProductId == productId)
                .Select(x => new ProductVariantDto
                {
                    Id = x.Id,
                    ProductId = x.ProductId,
                    Sku = x.Sku,
                    Barcode = x.Barcode,
                    Name = x.Name,
                    Price = x.Price,
                    OldPrice = x.OldPrice,
                    Weight = x.Weight,
                    Length = x.Length,
                    Width = x.Width,
                    Height = x.Height,
                    IsActive = x.IsActive
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ProductVariantDto> CreateAsync(
            Guid productId,
            CreateProductVariantRequest request,
            CancellationToken cancellationToken = default)
        {
            var productExists = await _dbContext.Products
                .AnyAsync(x => x.Id == productId, cancellationToken);

            if (!productExists)
                throw new InvalidOperationException("Product not found.");

            var sku = request.Sku.Trim();
            var name = request.Name.Trim();

            if (string.IsNullOrWhiteSpace(sku))
                throw new ArgumentException("SKU is required.");

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Variant name is required.");

            ValidatePrice(request.Price, request.OldPrice);

            var skuExists = await _dbContext.ProductVariants
                .AnyAsync(x => x.Sku == sku, cancellationToken);

            if (skuExists)
                throw new InvalidOperationException(
                    $"Product variant with SKU '{sku}' already exists.");

            var variant = new ProductVariant(
                Guid.NewGuid(),
                productId,
                sku,
                NormalizeOptional(request.Barcode),
                name,
                request.Price,
                request.OldPrice,
                request.CostPrice,
                request.Weight,
                request.Length,
                request.Width,
                request.Height,
                request.IsActive);

            _dbContext.ProductVariants.Add(variant);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(variant);
        }

        public async Task<ProductVariantDto?> UpdateAsync(
            Guid productId,
            Guid variantId,
            UpdateProductVariantRequest request,
            CancellationToken cancellationToken = default)
        {
            var variant = await _dbContext.ProductVariants
                .FirstOrDefaultAsync(
                    x => x.Id == variantId &&
                         x.ProductId == productId,
                    cancellationToken);

            if (variant is null)
                return null;

            var sku = request.Sku is null
                ? variant.Sku
                : request.Sku.Trim();

            if (string.IsNullOrWhiteSpace(sku))
                throw new ArgumentException("SKU cannot be empty.");

            if (request.Sku is not null)
            {
                var skuExists = await _dbContext.ProductVariants
                    .AnyAsync(
                        x => x.Id != variantId &&
                             x.Sku == sku,
                        cancellationToken);

                if (skuExists)
                    throw new InvalidOperationException(
                        $"Product variant with SKU '{sku}' already exists.");
            }

            var name = request.Name is null
                ? variant.Name
                : request.Name.Trim();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "Variant name cannot be empty.");

            var price = request.Price ?? variant.Price;

            var oldPrice = request.OldPrice ?? variant.OldPrice;

            ValidatePrice(price, oldPrice);

            variant.Update(
                sku,
                request.Barcode is null
                    ? variant.Barcode
                    : NormalizeOptional(request.Barcode),
                name,
                price,
                oldPrice,
                request.CostPrice ?? variant.CostPrice,
                request.Weight ?? variant.Weight,
                request.Length ?? variant.Length,
                request.Width ?? variant.Width,
                request.Height ?? variant.Height,
                request.IsActive ?? variant.IsActive);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(variant);
        }

        private static void ValidatePrice(
            decimal price,
            decimal? oldPrice)
        {
            if (price <= 0)
                throw new ArgumentException(
                    "Variant price must be greater than zero.");

            if (oldPrice.HasValue && oldPrice.Value <= price)
                throw new ArgumentException(
                    "Old price must be greater than current price.");
        }

        private static ProductVariantDto ToDto(
            ProductVariant variant)
        {
            return new ProductVariantDto
            {
                Id = variant.Id,
                ProductId = variant.ProductId,
                Sku = variant.Sku,
                Barcode = variant.Barcode,
                Name = variant.Name,
                Price = variant.Price,
                OldPrice = variant.OldPrice,
                Weight = variant.Weight,
                Length = variant.Length,
                Width = variant.Width,
                Height = variant.Height,
                IsActive = variant.IsActive
            };
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
