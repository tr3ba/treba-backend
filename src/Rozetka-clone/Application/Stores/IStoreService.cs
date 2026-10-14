using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Stores
{
    public interface IStoreService
    {
        Task<IReadOnlyList<StoreDto>> GetBySellerIdAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default
        );

        Task<StoreDto?> GetByIdAsync(
            Guid sellerId,
            Guid storeId,
            CancellationToken cancellationToken = default
        );

        Task<StoreDto> CreateAsync(
            Guid sellerId,
            CreateStoreRequest request,
            CancellationToken cancellationToken = default
        );

        Task<StoreDto?> UpdateAsync(
            Guid sellerId,
            Guid storeId,
            UpdateStoreRequest request,
            CancellationToken cancellationToken = default
        );
    }
}
