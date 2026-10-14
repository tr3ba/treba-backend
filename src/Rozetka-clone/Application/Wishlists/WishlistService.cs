using Application.Abstractions;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Wishlists
{
    public sealed class WishlistService : IWishlistService
    {
        private const string DefaultWishlistName = "Избранное";

        private readonly IApplicationDbContext _dbContext;

        public WishlistService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<WishlistDto> GetDefaultWishlistAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        )
        {
            var wishlist = await GetOrCreateDefaultWishlistAsync(
                userId,
                cancellationToken
            );

            var items = await (
                from wishlistItem in _dbContext.WishlistItems.AsNoTracking()
                join product in _dbContext.Products.AsNoTracking()
                    on wishlistItem.ProductId equals product.Id
                where wishlistItem.WishlistId == wishlist.Id
                orderby wishlistItem.AddedAt descending
                select new WishlistItemDto
                {
                    Id = wishlistItem.Id,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    AddedAt = wishlistItem.AddedAt
                }
            ).ToListAsync(cancellationToken);

            return new WishlistDto
            {
                Id = wishlist.Id,
                Name = wishlist.Name,
                DefaultWishlist = wishlist.DefaultWishlist,
                CreatedAt = wishlist.CreatedAt,
                Items = items
            };
        }

        public async Task AddProductAsync(
            Guid userId,
            Guid productId,
            CancellationToken cancellationToken = default
        )
        {
            var productExists = await _dbContext.Products
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == productId,
                    cancellationToken
                );

            if (!productExists)
            {
                throw new KeyNotFoundException("Product not found.");
            }

            var wishlist = await GetOrCreateDefaultWishlistAsync(
                userId,
                cancellationToken
            );

            var alreadyExists = await _dbContext.WishlistItems
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.WishlistId == wishlist.Id
                        && x.ProductId == productId,
                    cancellationToken
                );

            if (alreadyExists)
            {
                return;
            }

            var item = new WishlistItem
            {
                Id = Guid.NewGuid(),
                WishlistId = wishlist.Id,
                ProductId = productId,
                AddedAt = DateTime.UtcNow
            };

            _dbContext.WishlistItems.Add(item);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task RemoveProductAsync(
            Guid userId,
            Guid productId,
            CancellationToken cancellationToken = default
        )
        {
            var wishlist = await _dbContext.Wishlists
                .FirstOrDefaultAsync(
                    x =>
                        x.UserId == userId
                        && x.DefaultWishlist,
                    cancellationToken
                );

            if (wishlist is null)
            {
                return;
            }

            var item = await _dbContext.WishlistItems
                .FirstOrDefaultAsync(
                    x =>
                        x.WishlistId == wishlist.Id
                        && x.ProductId == productId,
                    cancellationToken
                );

            if (item is null)
            {
                return;
            }

            _dbContext.WishlistItems.Remove(item);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> ContainsProductAsync(
            Guid userId,
            Guid productId,
            CancellationToken cancellationToken = default
        )
        {
            return await (
                from wishlist in _dbContext.Wishlists.AsNoTracking()
                join item in _dbContext.WishlistItems.AsNoTracking()
                    on wishlist.Id equals item.WishlistId
                where
                    wishlist.UserId == userId
                    && wishlist.DefaultWishlist
                    && item.ProductId == productId
                select item.Id
            ).AnyAsync(cancellationToken);
        }

        private async Task<Wishlist> GetOrCreateDefaultWishlistAsync(
            Guid userId,
            CancellationToken cancellationToken
        )
        {
            var wishlist = await _dbContext.Wishlists
                .FirstOrDefaultAsync(
                    x =>
                        x.UserId == userId
                        && x.DefaultWishlist,
                    cancellationToken
                );

            if (wishlist is not null)
            {
                return wishlist;
            }

            wishlist = new Wishlist
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = DefaultWishlistName,
                DefaultWishlist = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Wishlists.Add(wishlist);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return wishlist;
        }
    }

}
