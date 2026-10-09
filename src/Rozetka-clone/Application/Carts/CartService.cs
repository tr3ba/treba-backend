using System;
using System.Collections.Generic;
using System.Text;
using Application.Abstractions;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Carts
{
    public sealed class CartService : ICartService
    {
        private readonly IApplicationDbContext _dbContext;

        public CartService(
            IApplicationDbContext dbContext
        )
        {
            _dbContext = dbContext;
        }

        public async Task<CartDto> GetAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        )
        {
            var cart = await _dbContext
                .Carts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UserId == userId,
                    cancellationToken
                );

            if (cart is null)
            {
                return new CartDto
                {
                    UserId = userId
                };
            }

            return await BuildCartDtoAsync(
                cart,
                cancellationToken
            );
        }

        public async Task<CartDto> AddItemAsync(
            Guid userId,
            AddCartItemRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (request.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            var userExists = await _dbContext.Users.AnyAsync(
                x => x.Id == userId,
                cancellationToken
            );

            if (!userExists)
            {
                throw new KeyNotFoundException("User not found.");
            }

            var variant = await _dbContext
                .ProductVariants
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.ProductVariantId,
                    cancellationToken
                );

            if (variant is null)
            {
                throw new KeyNotFoundException("Product variant not found.");
            }

            if (!variant.IsActive)
            {
                throw new InvalidOperationException("Product variant is not active.");
            }

            var cart = await _dbContext.Carts.FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken
            );

            var now = DateTime.UtcNow;

            if (cart is null)
            {
                cart = new Cart
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                _dbContext.Carts.Add(cart);
            }

            var existingItem = await _dbContext.CartItems.FirstOrDefaultAsync(
                x =>
                    x.CartId == cart.Id
                    && x.ProductVariantId == request.ProductVariantId,
                cancellationToken
            );

            if (existingItem is null)
            {
                var item = new CartItem
                {
                    Id = Guid.NewGuid(),
                    CartId = cart.Id,
                    ProductVariantId = request.ProductVariantId,
                    Quantity = request.Quantity,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                _dbContext.CartItems.Add(item);
            }
            else
            {
                existingItem.Quantity += request.Quantity;
                existingItem.UpdatedAt = now;
            }

            cart.UpdatedAt = now;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return await BuildCartDtoAsync(
                cart,
                cancellationToken
            );
        }

        public async Task<CartDto> UpdateItemAsync(
            Guid userId,
            Guid cartItemId,
            UpdateCartItemRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (request.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            var cart = await _dbContext.Carts.FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken
            );

            if (cart is null)
            {
                throw new KeyNotFoundException("Cart not found.");
            }

            var item = await _dbContext.CartItems.FirstOrDefaultAsync(
                x =>
                    x.Id == cartItemId
                    && x.CartId == cart.Id,
                cancellationToken
            );

            if (item is null)
            {
                throw new KeyNotFoundException("Cart item not found.");
            }

            item.Quantity = request.Quantity;
            item.UpdatedAt = DateTime.UtcNow;
            cart.UpdatedAt = item.UpdatedAt;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return await BuildCartDtoAsync(
                cart,
                cancellationToken
            );
        }

        public async Task<CartDto> RemoveItemAsync(
            Guid userId,
            Guid cartItemId,
            CancellationToken cancellationToken = default
        )
        {
            var cart = await _dbContext.Carts.FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken
            );

            if (cart is null)
            {
                throw new KeyNotFoundException("Cart not found.");
            }

            var item = await _dbContext.CartItems.FirstOrDefaultAsync(
                x =>
                    x.Id == cartItemId
                    && x.CartId == cart.Id,
                cancellationToken
            );

            if (item is null)
            {
                throw new KeyNotFoundException("Cart item not found.");
            }

            _dbContext.CartItems.Remove(item);

            cart.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return await BuildCartDtoAsync(
                cart,
                cancellationToken
            );
        }

        public async Task ClearAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        )
        {
            var cart = await _dbContext.Carts.FirstOrDefaultAsync(
                x => x.UserId == userId,
                cancellationToken
            );

            if (cart is null)
            {
                return;
            }

            var items = await _dbContext.CartItems
                .Where(x => x.CartId == cart.Id)
                .ToListAsync(cancellationToken);

            _dbContext.CartItems.RemoveRange(items);

            cart.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task<CartDto> BuildCartDtoAsync(
            Cart cart,
            CancellationToken cancellationToken
        )
        {
            var items = await (
                from cartItem in _dbContext.CartItems.AsNoTracking()
                join variant in _dbContext.ProductVariants.AsNoTracking() on cartItem.ProductVariantId equals variant.Id
                join product in _dbContext.Products.AsNoTracking() on variant.ProductId equals product.Id
                where cartItem.CartId == cart.Id
                select new CartItemDto
                {
                    Id = cartItem.Id,
                    ProductVariantId = variant.Id,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    VariantName = variant.Name,
                    Price = variant.Price,
                    OldPrice = variant.OldPrice,
                    Quantity = cartItem.Quantity,
                    TotalPrice = variant.Price * cartItem.Quantity,
                    IsAvailable = variant.IsActive,
                }
            ).ToListAsync(cancellationToken);

            return new CartDto
            {
                Id = cart.Id,
                UserId = cart.UserId,
                Items = items,
                TotalQuantity = items.Sum(x => x.Quantity),
                TotalPrice = items.Sum(x => x.TotalPrice),
            };
        }
    }
}
