using System;
using System.Collections.Generic;
using System.Text;
using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Stores
{
    public sealed class StoreService : IStoreService
    {
        private readonly IApplicationDbContext _dbContext;

        public StoreService(
            IApplicationDbContext dbContext
        )
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<StoreDto>> GetBySellerIdAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default
        )
        {
            return await _dbContext
                .Stores
                .AsNoTracking()
                .Where(x => x.SellerId == sellerId)
                .OrderBy(x => x.Name)
                .Select(
                    x =>
                        new StoreDto
                        {
                            Id = x.Id,
                            SellerId = x.SellerId,
                            Name = x.Name,
                            Slug = x.Slug,
                            Description = x.Description,
                            LogoUrl = x.LogoUrl,
                            IsActive = x.IsActive,
                            CreatedAt = x.CreatedAt,
                            UpdatedAt = x.UpdatedAt,
                        }
                )
                .ToListAsync(cancellationToken);
        }

        public async Task<StoreDto?> GetByIdAsync(
            Guid sellerId,
            Guid storeId,
            CancellationToken cancellationToken = default
        )
        {
            return await _dbContext
                .Stores
                .AsNoTracking()
                .Where(
                    x =>
                        x.Id == storeId
                        && x.SellerId == sellerId
                )
                .Select(
                    x =>
                        new StoreDto
                        {
                            Id = x.Id,
                            SellerId = x.SellerId,
                            Name = x.Name,
                            Slug = x.Slug,
                            Description = x.Description,
                            LogoUrl = x.LogoUrl,
                            IsActive = x.IsActive,
                            CreatedAt = x.CreatedAt,
                            UpdatedAt = x.UpdatedAt,
                        }
                )
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<StoreDto> CreateAsync(
            Guid sellerId,
            CreateStoreRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var seller = await _dbContext
                .Sellers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == sellerId,
                    cancellationToken
                );

            if (seller is null)
            {
                throw new InvalidOperationException("Seller not found.");
            }

            if (seller.Status != SellerStatus.ACTIVE)
            {
                throw new InvalidOperationException("Only active sellers can create stores.");
            }

            var name = NormalizeRequired(
                request.Name,
                "Name"
            );
            var slug = NormalizeSlug(request.Slug);

            var slugExists = await _dbContext.Stores.AnyAsync(
                x => x.Slug == slug,
                cancellationToken
            );

            if (slugExists)
            {
                throw new InvalidOperationException("Store with this slug already exists.");
            }

            var now = DateTime.UtcNow;

            var store = new Store
            {
                Id = Guid.NewGuid(),
                SellerId = sellerId,
                Name = name,
                Slug = slug,
                Description = NormalizeOptional(request.Description),
                LogoUrl = NormalizeOptional(request.LogoUrl),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _dbContext.Stores.Add(store);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(store);
        }

        public async Task<StoreDto?> UpdateAsync(
            Guid sellerId,
            Guid storeId,
            UpdateStoreRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var store = await _dbContext.Stores.FirstOrDefaultAsync(
                x =>
                    x.Id == storeId
                    && x.SellerId == sellerId,
                cancellationToken
            );

            if (store is null)
            {
                return null;
            }

            if (request.Name is not null)
            {
                store.Name = NormalizeRequired(
                    request.Name,
                    "Name"
                );
            }

            if (request.Slug is not null)
            {
                var slug = NormalizeSlug(request.Slug);

                var slugExists = await _dbContext.Stores.AnyAsync(
                    x =>
                        x.Id != storeId
                        && x.Slug == slug,
                    cancellationToken
                );

                if (slugExists)
                {
                    throw new InvalidOperationException("Store with this slug already exists.");
                }

                store.Slug = slug;
            }

            if (request.Description is not null)
            {
                store.Description = NormalizeOptional(request.Description);
            }

            if (request.LogoUrl is not null)
            {
                store.LogoUrl = NormalizeOptional(request.LogoUrl);
            }

            if (request.IsActive.HasValue)
            {
                store.IsActive = request.IsActive.Value;
            }

            store.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(store);
        }

        private static StoreDto ToDto(
            Store store
        )
        {
            return new StoreDto
            {
                Id = store.Id,
                SellerId = store.SellerId,
                Name = store.Name,
                Slug = store.Slug,
                Description = store.Description,
                LogoUrl = store.LogoUrl,
                IsActive = store.IsActive,
                CreatedAt = store.CreatedAt,
                UpdatedAt = store.UpdatedAt,
            };
        }

        private static string NormalizeRequired(
            string value,
            string fieldName
        )
        {
            var normalized = value.Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException($"{fieldName} is required.");
            }

            return normalized;
        }

        private static string NormalizeSlug(
            string slug
        )
        {
            var normalized = slug
                .Trim()
                .ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Slug is required.");
            }

            return normalized;
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
