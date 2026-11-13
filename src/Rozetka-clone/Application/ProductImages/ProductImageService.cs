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
        private readonly IApplicationTransactionManager _transactions;
        private readonly IProductMediaUrls? _urls;

        public ProductImageService(
            IApplicationDbContext dbContext,
            IApplicationTransactionManager transactions,
            IProductMediaUrls? urls = null
        )
        {
            _dbContext = dbContext;
            _transactions = transactions;
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


        public async Task<IReadOnlyList<ProductImageChangeDto>> GetChangesAsync(
            Guid productId,
            CancellationToken cancellationToken = default
        )
        {
            var changes = await _dbContext.ProductImageChanges
                .AsNoTracking()
                .Where(x => x.ProductId == productId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new ProductImageChangeDto
                {
                    Id = x.Id,
                    ProductId = x.ProductId,
                    ImageId = x.ImageId,
                    ChangeType = x.ChangeType,
                    Status = x.Status,
                    VariantId = x.VariantId,
                    ImageUrl = x.ImageUrl,
                    AltText = x.AltText,
                    SortOrder = x.SortOrder,
                    IsMain = x.IsMain,
                    CreatedAt = x.CreatedAt,
                    ReviewedAt = x.ReviewedAt,
                    RejectionReason = x.RejectionReason
                })
                .ToListAsync(cancellationToken);

            return changes;
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

            if (mainImages.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }


        public async Task<ProductImageChangeDto> SubmitCreateAsync(
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

            var imageUrl = ProductImageUrls.Validate(
                productId,
                request.ImageUrl
            );

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                throw new ArgumentException("Image URL is required.");
            }

            var change = new ProductImageChange
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                ChangeType = ProductImageChangeType.Create,
                Status = ProductImageChangeStatus.Pending,
                VariantId = request.VariantId,
                ImageUrl = imageUrl,
                AltText = NormalizeOptional(request.AltText),
                SortOrder = request.SortOrder,
                IsMain = request.IsMain,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ProductImageChanges.Add(change);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ProductImageChangeDto
            {
                Id = change.Id,
                ProductId = change.ProductId,
                ImageId = change.ImageId,
                ChangeType = change.ChangeType,
                Status = change.Status,
                VariantId = change.VariantId,
                ImageUrl = change.ImageUrl,
                AltText = change.AltText,
                SortOrder = change.SortOrder,
                IsMain = change.IsMain,
                CreatedAt = change.CreatedAt
            };
        }


        public async Task<ProductImageChangeDto?> SubmitUpdateAsync(
            Guid productId,
            Guid imageId,
            UpdateProductImageRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var image = await _dbContext.ProductImages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == imageId && x.ProductId == productId,
                    cancellationToken
                );

            if (image is null)
            {
                return null;
            }

            var hasPendingChange = await _dbContext.ProductImageChanges
                .AnyAsync(
                    x => x.ImageId == imageId
                        && x.Status == ProductImageChangeStatus.Pending,
                    cancellationToken
                );

            if (hasPendingChange)
            {
                throw new BusinessRuleException(
                    "This image already has a pending moderation request."
                );
            }

            var variantId = request.VariantId ?? image.VariantId;

            await ValidateVariantAsync(
                productId,
                variantId,
                cancellationToken
            );

            var imageUrl = image.ImageUrl;

            if (request.ImageUrl is not null)
            {
                imageUrl = request.ImageUrl.Trim().Split('?', 2)[0]
                    == ProductImageUrls.ForBrowser(image.ProductId, image.Id, image.ImageUrl)
                        ? image.ImageUrl
                        : ProductImageUrls.Validate(productId, request.ImageUrl);

                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    throw new ArgumentException("Image URL cannot be empty.");
                }
            }

            var change = new ProductImageChange
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                ImageId = imageId,
                ChangeType = ProductImageChangeType.Update,
                Status = ProductImageChangeStatus.Pending,
                VariantId = variantId,
                ImageUrl = imageUrl,
                AltText = request.AltText is null
                    ? image.AltText
                    : NormalizeOptional(request.AltText),
                SortOrder = request.SortOrder ?? image.SortOrder,
                IsMain = request.IsMain ?? image.IsMain,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ProductImageChanges.Add(change);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ProductImageChangeDto
            {
                Id = change.Id,
                ProductId = change.ProductId,
                ImageId = change.ImageId,
                ChangeType = change.ChangeType,
                Status = change.Status,
                VariantId = change.VariantId,
                ImageUrl = change.ImageUrl,
                AltText = change.AltText,
                SortOrder = change.SortOrder,
                IsMain = change.IsMain,
                CreatedAt = change.CreatedAt
            };
        }


        public async Task<ProductImageChangeDto?> SubmitDeleteAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default
        )
        {
            var image = await _dbContext.ProductImages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == imageId && x.ProductId == productId,
                    cancellationToken
                );

            if (image is null)
            {
                return null;
            }

            var hasPendingChange = await _dbContext.ProductImageChanges
                .AnyAsync(
                    x => x.ImageId == imageId
                        && x.Status == ProductImageChangeStatus.Pending,
                    cancellationToken
                );

            if (hasPendingChange)
            {
                throw new BusinessRuleException(
                    "This image already has a pending moderation request."
                );
            }

            var change = new ProductImageChange
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                ImageId = imageId,
                ChangeType = ProductImageChangeType.Delete,
                Status = ProductImageChangeStatus.Pending,
                VariantId = image.VariantId,
                ImageUrl = image.ImageUrl,
                AltText = image.AltText,
                SortOrder = image.SortOrder,
                IsMain = image.IsMain,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ProductImageChanges.Add(change);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ProductImageChangeDto
            {
                Id = change.Id,
                ProductId = change.ProductId,
                ImageId = change.ImageId,
                ChangeType = change.ChangeType,
                Status = change.Status,
                VariantId = change.VariantId,
                ImageUrl = change.ImageUrl,
                AltText = change.AltText,
                SortOrder = change.SortOrder,
                IsMain = change.IsMain,
                CreatedAt = change.CreatedAt
            };
        }


        public async Task<ProductImageChangeDto?> ApproveChangeAsync(
            Guid productId,
            Guid changeId,
            CancellationToken cancellationToken = default
        )
        {
            await using var transaction = await _transactions.BeginForProductAsync(
                productId,
                cancellationToken
            );

            var change = await _dbContext.ProductImageChanges
                .SingleOrDefaultAsync(
                    x => x.Id == changeId && x.ProductId == productId,
                    cancellationToken
                );

            if (change is null)
            {
                return null;
            }

            if (change.Status != ProductImageChangeStatus.Pending)
            {
                throw new BusinessRuleException(
                    "Only pending image changes can be approved."
                );
            }

            switch (change.ChangeType)
            {
                case ProductImageChangeType.Create:
                    {
                        if (string.IsNullOrWhiteSpace(change.ImageUrl))
                        {
                            throw new BusinessRuleException("Image URL is required.");
                        }

                        var image = new ProductImage
                        {
                            Id = Guid.NewGuid(),
                            ProductId = productId,
                            VariantId = change.VariantId,
                            ImageUrl = change.ImageUrl,
                            AltText = change.AltText,
                            SortOrder = change.SortOrder ?? 0,
                            IsMain = change.IsMain ?? false
                        };

                        if (image.IsMain)
                        {
                            await ResetMainImageAsync(productId, cancellationToken);
                        }

                        _dbContext.ProductImages.Add(image);
                        change.ImageId = image.Id;
                        break;
                    }

                case ProductImageChangeType.Update:
                    {
                        var image = await _dbContext.ProductImages
                            .SingleOrDefaultAsync(
                                x => x.Id == change.ImageId && x.ProductId == productId,
                                cancellationToken
                            );

                        if (image is null)
                        {
                            throw new BusinessRuleException("Image not found.");
                        }

                        if (string.IsNullOrWhiteSpace(change.ImageUrl))
                        {
                            throw new BusinessRuleException("Image URL is required.");
                        }

                        if (change.IsMain == true)
                        {
                            await ResetMainImageAsync(
                                productId,
                                cancellationToken,
                                image.Id
                            );
                        }

                        image.VariantId = change.VariantId;
                        image.ImageUrl = change.ImageUrl;
                        image.AltText = change.AltText;
                        image.SortOrder = change.SortOrder ?? 0;
                        image.IsMain = change.IsMain ?? false;
                        break;
                    }

                case ProductImageChangeType.Delete:
                    {
                        var image = await _dbContext.ProductImages
                            .SingleOrDefaultAsync(
                                x => x.Id == change.ImageId && x.ProductId == productId,
                                cancellationToken
                            );

                        if (image is null)
                        {
                            throw new BusinessRuleException("Image not found.");
                        }

                        _dbContext.ProductImages.Remove(image);
                        break;
                    }

                default:
                    throw new BusinessRuleException("Unsupported image change type.");
            }

            change.Status = ProductImageChangeStatus.Approved;
            change.ReviewedAt = DateTime.UtcNow;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    "Image change was already reviewed by another moderator."
                );
            }

            await transaction.CommitAsync(cancellationToken);

            return new ProductImageChangeDto
            {
                Id = change.Id,
                ProductId = change.ProductId,
                ImageId = change.ImageId,
                ChangeType = change.ChangeType,
                Status = change.Status,
                VariantId = change.VariantId,
                ImageUrl = change.ImageUrl,
                AltText = change.AltText,
                SortOrder = change.SortOrder,
                IsMain = change.IsMain,
                CreatedAt = change.CreatedAt,
                ReviewedAt = change.ReviewedAt,
                RejectionReason = change.RejectionReason
            };
        }


        public async Task<ProductImageChangeDto?> RejectChangeAsync(
            Guid productId,
            Guid changeId,
            string reason,
            CancellationToken cancellationToken = default
        )
        {
            await using var transaction = await _transactions.BeginForProductAsync(
                productId,
                cancellationToken
            );

            var change = await _dbContext.ProductImageChanges
                .SingleOrDefaultAsync(
                    x => x.Id == changeId && x.ProductId == productId,
                    cancellationToken
                );

            if (change is null)
            {
                return null;
            }

            if (change.Status != ProductImageChangeStatus.Pending)
            {
                throw new BusinessRuleException(
                    "Only pending image changes can be rejected."
                );
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Rejection reason is required.");
            }

            var normalizedReason = reason.Trim();

            if (normalizedReason.Length > 1000)
            {
                throw new ArgumentException(
                    "Rejection reason cannot exceed 1000 characters."
                );
            }

            change.Status = ProductImageChangeStatus.Rejected;
            change.RejectionReason = normalizedReason;
            change.ReviewedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ProductImageChangeDto
            {
                Id = change.Id,
                ProductId = change.ProductId,
                ImageId = change.ImageId,
                ChangeType = change.ChangeType,
                Status = change.Status,
                VariantId = change.VariantId,
                ImageUrl = change.ImageUrl,
                AltText = change.AltText,
                SortOrder = change.SortOrder,
                IsMain = change.IsMain,
                CreatedAt = change.CreatedAt,
                ReviewedAt = change.ReviewedAt,
                RejectionReason = change.RejectionReason
            };
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
