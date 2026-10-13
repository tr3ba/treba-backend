using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Wishlists
{
    public interface IWishlistService
    {
        Task<WishlistDto> GetDefaultWishlistAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        );

        Task AddProductAsync(
            Guid userId,
            Guid productId,
            CancellationToken cancellationToken = default
        );

        Task RemoveProductAsync(
            Guid userId,
            Guid productId,
            CancellationToken cancellationToken = default
        );

        Task<bool> ContainsProductAsync(
            Guid userId,
            Guid productId,
            CancellationToken cancellationToken = default
        );
    }

}
