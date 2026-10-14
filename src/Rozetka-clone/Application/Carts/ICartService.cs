using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Carts
{
    public interface ICartService
    {
        Task<CartDto> GetAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        );

        Task<CartDto> AddItemAsync(
            Guid userId,
            AddCartItemRequest request,
            CancellationToken cancellationToken = default
        );

        Task<CartDto> UpdateItemAsync(
            Guid userId,
            Guid cartItemId,
            UpdateCartItemRequest request,
            CancellationToken cancellationToken = default
        );

        Task<CartDto> RemoveItemAsync(
            Guid userId,
            Guid cartItemId,
            CancellationToken cancellationToken = default
        );

        Task ClearAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        );
    }
}
