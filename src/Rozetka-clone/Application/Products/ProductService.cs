using System;
using System.Collections.Generic;
using System.Text;
using Application.Abstractions;
using Application.Common;
using Domain.Entities.Product;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Contracts.Admin.Settings;
using Domain.Entities.Users;

namespace Application.Products
{
    public sealed class ProductService : IProductService
    {
        private readonly IApplicationDbContext _dbContext;
        private readonly ICurrentActor? _actor;

        public ProductService(
            IApplicationDbContext dbContext,
            ICurrentActor? actor = null
        )
        {
            _dbContext = dbContext;
            _actor = actor;
        }

        public async Task<IReadOnlyList<ProductDto>> GetAllAsync(
            CancellationToken cancellationToken = default
        )
        {
            return await _dbContext
                .Products
                .AsNoTracking()
                .Where(
                    x =>
                        x.Status == ProductStatus.ACTIVE
                        && _dbContext.Categories.Any(
                            c =>
                                c.Id == x.CategoryId
                                && c.IsActive
                        )
                        && _dbContext.Brands.Any(
                            b =>
                                b.Id == x.BrandId
                                && b.IsActive
                        )
                )
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => ToDto(x))
                .ToListAsync(cancellationToken);
        }

        public async Task<ProductDto?> GetBySlugAsync(
            string slug,
            CancellationToken cancellationToken = default
        )
        {
            var product = await _dbContext
                .Products
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.Slug == slug
                        && x.Status == ProductStatus.ACTIVE
                        && _dbContext.Categories.Any(
                            c =>
                                c.Id == x.CategoryId
                                && c.IsActive
                        )
                        && _dbContext.Brands.Any(
                            b =>
                                b.Id == x.BrandId
                                && b.IsActive
                        ),
                    cancellationToken
                );

            return product is null
                ? null
                : ToDto(product);
        }

        public async Task<ProductDto> CreateAsync(
            CreateProductRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var slugExists = await _dbContext.Products.AnyAsync(
                x =>
                    x.Slug == request.Slug
                        .Trim()
                        .ToLowerInvariant(),
                cancellationToken
            );

            if (slugExists)
            {
                throw new BusinessRuleException($"Product with slug '{request.Slug}' already exists.");
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                StoreId = request.StoreId,
                CategoryId = request.CategoryId,
                BrandId = request.BrandId,

                Name = request.Name.Trim(),
                Slug = request.Slug
                    .Trim()
                    .ToLowerInvariant(),

                ShortDescription = request.ShortDescription.Trim(),
                Description = request.Description.Trim(),

                Status = ProductStatus.DRAFT,

                AverageRating = 0,
                ReviewCount = 0,
                SalesCount = 0,

                WarrantyMonths = request.WarrantyMonth,
                CountryOfOrigin = request.CountryOfOrigin.Trim(),

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            _dbContext.Products.Add(product);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(product);
        }

        public async Task<ProductDto?> UpdateAsync(
            Guid id,
            UpdateProductRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var product = await _dbContext.Products.FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken
            );

            if (product is null)
            {
                return null;
            }

            if (product.Status is ProductStatus.PENDING_MODERATION or ProductStatus.ARCHIVED)
            {
                throw new BusinessRuleException("A product under review or archived cannot be edited.");
            }

            if (request.CategoryId.HasValue)
            {
                product.CategoryId = request.CategoryId.Value;
            }

            if (request.BrandId.HasValue)
            {
                product.BrandId = request.BrandId.Value;
            }

            if (request.Name is not null)
            {
                product.Name = request.Name.Trim();
            }

            if (request.Slug is not null)
            {
                var slug = request.Slug
                    .Trim()
                    .ToLowerInvariant();

                var exists = await _dbContext.Products.AnyAsync(
                    x =>
                        x.Id != id
                        && x.Slug == slug,
                    cancellationToken
                );

                if (exists)
                {
                    throw new BusinessRuleException($"Product with slug '{slug}' already exists.");
                }

                product.Slug = slug;
            }

            if (request.ShortDescription is not null)
            {
                product.ShortDescription = request.ShortDescription.Trim();
            }

            if (request.Description is not null)
            {
                product.Description = request.Description.Trim();
            }

            if (request.WarrantyMonth.HasValue)
            {
                product.WarrantyMonths = request.WarrantyMonth.Value;
            }

            if (request.CountryOfOrigin is not null)
            {
                product.CountryOfOrigin = request.CountryOfOrigin.Trim();
            }

            product.Status = ProductStatus.DRAFT;
            product.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(product);
        }

        public async Task<bool> SubmitForModerationAsync(
            Guid id,
            CancellationToken cancellationToken = default
        )
        {
            var product = await FindProductAsync(
                id,
                cancellationToken
            );

            if (product is null)
            {
                return false;
            }

            if (product.Status != ProductStatus.DRAFT
                && product.Status != ProductStatus.REJECTED)
            {
                throw new BusinessRuleException("Only draft or rejected product can be submitted for moderation.");
            }

            var variants = await _dbContext
                .ProductVariants
                .AsNoTracking()
                .Where(
                    v =>
                        v.ProductId == id
                        && v.IsActive
                )
                .Select(v => v.Price)
                .ToListAsync(cancellationToken);
            if (!variants.Any(price => price > 0))
            {
                throw new BusinessRuleException("Add an active variant with a positive price before submitting.");
            }

            if (!await _dbContext.Categories.AnyAsync(
                c =>
                    c.Id == product.CategoryId
                    && c.IsActive,
                cancellationToken
            ))
            {
                throw new BusinessRuleException("Product category must be active.");
            }

            if (!await _dbContext.Brands.AnyAsync(
                b =>
                    b.Id == product.BrandId
                    && b.IsActive,
                cancellationToken
            ))
            {
                throw new BusinessRuleException("Product brand must be active.");
            }

            var previous = product.Status;
            product.Status = ProductStatus.PENDING_MODERATION;
            product.UpdatedAt = DateTime.UtcNow;
            RecordDecision(product, previous, "Submitted", null);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        public async Task<bool> ApproveAsync(
            Guid id,
            CancellationToken cancellationToken = default
        )
        {
            var product = await FindProductAsync(
                id,
                cancellationToken
            );

            if (product is null)
            {
                return false;
            }

            if (product.Status != ProductStatus.PENDING_MODERATION)
            {
                throw new BusinessRuleException("Product must be pending moderation.");
            }

            var previous = product.Status;
            product.Status = ProductStatus.ACTIVE;
            product.UpdatedAt = DateTime.UtcNow;
            RecordDecision(product, previous, "Approved", null);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        public async Task<bool> RejectAsync(
            Guid id,
            string reason,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
                throw new ArgumentException("Enter a rejection reason of up to 2000 characters.");
            var product = await FindProductAsync(
                id,
                cancellationToken
            );

            if (product is null)
            {
                return false;
            }

            if (product.Status != ProductStatus.PENDING_MODERATION)
            {
                throw new BusinessRuleException("Product must be pending moderation.");
            }

            var previous = product.Status;
            product.Status = ProductStatus.REJECTED;
            product.UpdatedAt = DateTime.UtcNow;
            RecordDecision(product, previous, "Rejected", reason.Trim());

            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }

        private void RecordDecision(Product product, ProductStatus previous, string decision, string? reason)
        {
            var name = _actor?.Name ?? "System";
            var changes = new List<AuditChangeDto> { new("Status", previous.ToString(), product.Status.ToString()), new("Decision", null, decision) };
            if (reason is not null) changes.Add(new("Reason", null, reason));
            _dbContext.AdminAuditLogs.Add(new AdminAuditLog {
                EntityId = product.Id, EntityType = "Product", EntityName = product.Name[..Math.Min(500, product.Name.Length)],
                ActorId = _actor?.UserId ?? Guid.Empty, ActorName = name[..Math.Min(name.Length, 320)], ActorRole = _actor?.Role ?? "System",
                Action = "Moderation", ChangesJson = JsonSerializer.Serialize(changes) });
        }

        private Task<Product?> FindProductAsync(
            Guid id,
            CancellationToken cancellationToken
        )
        {
            return _dbContext.Products.FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken
            );
        }

        private static ProductDto ToDto(
            Product product
        )
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

                WarrantyMonth = product.WarrantyMonths,
                CountryOfOrigin = product.CountryOfOrigin,

                CreatedAt = product.CreatedAt,
                UpdatedAt = product.UpdatedAt,
            };
        }
    }
}
