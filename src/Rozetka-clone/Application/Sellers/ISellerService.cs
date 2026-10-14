using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Sellers
{
    public interface ISellerService
    {
        Task<SellerDto> CreateAsync(
            CreateSellerRequest request,
            CancellationToken cancellationToken = default
        );

        Task<SellerDto?> GetByIdAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default
        );

        Task<SellerDto?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default
        );

        Task<IReadOnlyList<SellerDto>> GetAllAsync(
            CancellationToken cancellationToken = default
        );

        Task<SellerDto?> UpdateAsync(
            Guid sellerId,
            UpdateSellerRequest request,
            CancellationToken cancellationToken = default
        );

        Task<SellerDto?> ApproveAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default
        );

        Task<SellerDto?> SuspendAsync(
            Guid sellerId,
            CancellationToken cancellationToken = default
        );
    }
}
