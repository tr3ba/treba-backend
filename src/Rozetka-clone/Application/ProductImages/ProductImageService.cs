using Application.Abstractions;
using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductImages
{
    public sealed class ProductImageService : IProductImageService
    {
        private readonly IApplicationDbContext _dbContext;

        public ProductImageService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<ProductImageDto>> GetByProductIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ProductImages
                .AsNoTracking()
                .Where(x => x.ProductId == productId)
                .OrderByDescending(x => x.IsMain)
                .ThenBy(x => x.SortOrder)
                .Select(x => new ProductImageDto
                {
                    Id = x.Id,
                    ProductId = x.ProductId,
                    VariantId = x.VariantId,
                    ImageUrl = x.ImageUrl,
                    AltText = x.AltText,
                    SortOrder = x.SortOrder,
                    IsMain = x.IsMain
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<ProductImageDto?> GetByIdAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ProductImages
                .AsNoTracking()
                .Where(x => x.Id == imageId &&
                            x.ProductId == productId)
                .Select(x => new ProductImageDto
                {
                    Id = x.Id,
                    ProductId = x.ProductId,
                    VariantId = x.VariantId,
                    ImageUrl = x.ImageUrl,
                    AltText = x.AltText,
                    SortOrder = x.SortOrder,
                    IsMain = x.IsMain
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ProductImageDto> CreateAsync(
            Guid productId,
            CreateProductImageRequest request,
            CancellationToken cancellationToken = default)
        {
            var productExists = await _dbContext.Products
                .AnyAsync(
                    x => x.Id == productId,
                    cancellationToken);

            if (!productExists)
                throw new InvalidOperationException("Product not found.");

            await ValidateVariantAsync(
                productId,
                request.VariantId,
                cancellationToken);

            var imageUrl = request.ImageUrl.Trim();

            if (string.IsNullOrWhiteSpace(imageUrl))
                throw new ArgumentException("Image URL is required.");

            if (request.IsMain)
            {
                await ResetMainImageAsync(
                    productId,
                    cancellationToken);
            }

            var image = new ProductImage
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                VariantId = request.VariantId,
                ImageUrl = imageUrl,
                AltText = NormalizeOptional(request.AltText),
                SortOrder = request.SortOrder,
                IsMain = request.IsMain
            };

            _dbContext.ProductImages.Add(image);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(image);
        }

        public async Task<ProductImageDto?> UpdateAsync(
            Guid productId,
            Guid imageId,
            UpdateProductImageRequest request,
            CancellationToken cancellationToken = default)
        {
            var image = await _dbContext.ProductImages
                .FirstOrDefaultAsync(
                    x => x.Id == imageId &&
                         x.ProductId == productId,
                    cancellationToken);

            if (image is null)
                return null;

            if (request.VariantId.HasValue)
            {
                await ValidateVariantAsync(
                    productId,
                    request.VariantId,
                    cancellationToken);

                image.VariantId = request.VariantId;
            }

            if (request.ImageUrl is not null)
            {
                var imageUrl = request.ImageUrl.Trim();

                if (string.IsNullOrWhiteSpace(imageUrl))
                    throw new ArgumentException(
                        "Image URL cannot be empty.");

                image.ImageUrl = imageUrl;
            }

            if (request.AltText is not null)
                image.AltText = NormalizeOptional(request.AltText);

            if (request.SortOrder.HasValue)
                image.SortOrder = request.SortOrder.Value;

            if (request.IsMain.HasValue)
            {
                if (request.IsMain.Value)
                {
                    await ResetMainImageAsync(
                        productId,
                        cancellationToken,
                        imageId);
                }

                image.IsMain = request.IsMain.Value;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(image);
        }

        public async Task<bool> DeleteAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default)
        {
            var image = await _dbContext.ProductImages
                .FirstOrDefaultAsync(
                    x => x.Id == imageId &&
                         x.ProductId == productId,
                    cancellationToken);

            if (image is null)
                return false;

            _dbContext.ProductImages.Remove(image);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        private async Task ValidateVariantAsync(
            Guid productId,
            Guid? variantId,
            CancellationToken cancellationToken)
        {
            if (!variantId.HasValue)
                return;

            var variantExists = await _dbContext.ProductVariants
                .AnyAsync(
                    x => x.Id == variantId.Value &&
                         x.ProductId == productId,
                    cancellationToken);

            if (!variantExists)
            {
                throw new InvalidOperationException(
                    "Product variant not found or does not belong to this product.");
            }
        }

        private async Task ResetMainImageAsync(
            Guid productId,
            CancellationToken cancellationToken,
            Guid? exceptImageId = null)
        {
            var mainImages = await _dbContext.ProductImages
                .Where(x =>
                    x.ProductId == productId &&
                    x.IsMain &&
                    (!exceptImageId.HasValue ||
                     x.Id != exceptImageId.Value))
                .ToListAsync(cancellationToken);

            foreach (var image in mainImages)
                image.IsMain = false;
        }

        private static ProductImageDto ToDto(ProductImage image)
        {
            return new ProductImageDto
            {
                Id = image.Id,
                ProductId = image.ProductId,
                VariantId = image.VariantId,
                ImageUrl = image.ImageUrl,
                AltText = image.AltText,
                SortOrder = image.SortOrder,
                IsMain = image.IsMain
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
