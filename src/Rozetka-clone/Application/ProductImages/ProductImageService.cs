using System;
using System.Collections.Generic;
using System.Text;
using Application.Abstractions;
using Application.Common;
using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;

namespace Application.ProductImages
{
    public sealed class ProductImageService : IProductImageService
    {
        private readonly IApplicationDbContext _dbContext;
        private readonly IProductMediaUrls? _urls;

        public ProductImageService(
            IApplicationDbContext dbContext, IProductMediaUrls? urls = null
        )
        {
            _dbContext = dbContext;
            _urls = urls;
        }

        public async Task<IReadOnlyList<ProductImageDto>> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            var images = await _dbContext.ProductImages.AsNoTracking().Where(i => i.ProductId == productId)
                .OrderByDescending(i => i.IsMain).ThenBy(i => i.SortOrder).ToListAsync(cancellationToken);
            return images.Select(ToDto).ToList();
        }

        public async Task<ProductImageDto?> GetByIdAsync(Guid productId, Guid imageId, CancellationToken cancellationToken = default)
        {
            var image = await _dbContext.ProductImages.AsNoTracking()
                .SingleOrDefaultAsync(i => i.ProductId == productId && i.Id == imageId, cancellationToken);
            return image is null ? null : ToDto(image);
        }

        public async Task<ProductImageDto> CreateAsync(
            Guid productId,
            CreateProductImageRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var productExists = await _dbContext.Products.AnyAsync(
                x => x.Id == productId,
                cancellationToken
            );

            if (!productExists)
            {
                throw new BusinessRuleException("Product not found.");
            }

            await ValidateVariantAsync(
                productId,
                request.VariantId,
                cancellationToken
            );

            var imageUrl = ProductImageUrls.Validate(productId, request.ImageUrl);

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                throw new ArgumentException("Image URL is required.");
            }

            if (request.IsMain)
            {
                await ResetMainImageAsync(
                    productId,
                    cancellationToken
                );
            }

            var image = new ProductImage
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                VariantId = request.VariantId,
                ImageUrl = imageUrl,
                AltText = NormalizeOptional(request.AltText),
                SortOrder = request.SortOrder,
                IsMain = request.IsMain,
            };

            _dbContext.ProductImages.Add(image);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(image);
        }

        public async Task<ProductImageDto?> UpdateAsync(
            Guid productId,
            Guid imageId,
            UpdateProductImageRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var image = await _dbContext.ProductImages.FirstOrDefaultAsync(
                x =>
                    x.Id == imageId
                    && x.ProductId == productId,
                cancellationToken
            );

            if (image is null)
            {
                return null;
            }

            if (request.VariantId.HasValue)
            {
                await ValidateVariantAsync(
                    productId,
                    request.VariantId,
                    cancellationToken
                );

                image.VariantId = request.VariantId;
            }

            if (request.ImageUrl is not null)
            {
                var imageUrl = request.ImageUrl.Trim().Split('?', 2)[0] == ProductImageUrls.ForBrowser(image.ProductId, image.Id, image.ImageUrl)
                    ? image.ImageUrl : ProductImageUrls.Validate(productId, request.ImageUrl);

                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    throw new ArgumentException("Image URL cannot be empty.");
                }

                image.ImageUrl = imageUrl;
            }

            if (request.AltText is not null)
            {
                image.AltText = NormalizeOptional(request.AltText);
            }

            if (request.SortOrder.HasValue)
            {
                image.SortOrder = request.SortOrder.Value;
            }

            if (request.IsMain.HasValue)
            {
                if (request.IsMain.Value)
                {
                    await ResetMainImageAsync(
                        productId,
                        cancellationToken,
                        imageId
                    );
                }

                image.IsMain = request.IsMain.Value;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(image);
        }

        public async Task<bool> DeleteAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default
        )
        {
            var image = await _dbContext.ProductImages.FirstOrDefaultAsync(
                x =>
                    x.Id == imageId
                    && x.ProductId == productId,
                cancellationToken
            );

            if (image is null)
            {
                return false;
            }

            _dbContext.ProductImages.Remove(image);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        private async Task ValidateVariantAsync(
            Guid productId,
            Guid? variantId,
            CancellationToken cancellationToken
        )
        {
            if (!variantId.HasValue)
            {
                return;
            }

            var variantExists = await _dbContext.ProductVariants.AnyAsync(
                x =>
                    x.Id == variantId.Value
                    && x.ProductId == productId,
                cancellationToken
            );

            if (!variantExists)
            {
                throw new BusinessRuleException("Product variant not found or does not belong to this product.");
            }
        }

        private async Task ResetMainImageAsync(
            Guid productId,
            CancellationToken cancellationToken,
            Guid? exceptImageId = null
        )
        {
            var mainImages = await _dbContext
                .ProductImages
                .Where(
                    x =>
                        x.ProductId == productId
                        && x.IsMain
                        && (
                            !exceptImageId.HasValue
                            || x.Id != exceptImageId.Value
                        )
                )
                .ToListAsync(cancellationToken);

            foreach (var image in mainImages)
            {
                image.IsMain = false;
            }
        }

        private ProductImageDto ToDto(
            ProductImage image
        )
        {
            return new ProductImageDto
            {
                Id = image.Id,
                ProductId = image.ProductId,
                VariantId = image.VariantId,
                ImageUrl = _urls?.ForCurrentActor(ProductImageUrls.ForBrowser(image.ProductId, image.Id, image.ImageUrl))
                    ?? ProductImageUrls.ForBrowser(image.ProductId, image.Id, image.ImageUrl),
                AltText = image.AltText,
                SortOrder = image.SortOrder,
                IsMain = image.IsMain,
            };
        }

        private static string? NormalizeOptional(
            string? value
        )
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
