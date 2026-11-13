using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductImages
{
    public interface IProductImageService
    {
        Task<IReadOnlyList<ProductImageDto>> GetByProductIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default
        );

        Task<ProductImageDto?> GetByIdAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default
        );

        Task<IReadOnlyList<ProductImageChangeDto>> GetChangesAsync(
            Guid productId,
            CancellationToken cancellationToken = default
        );

        Task<ProductImageChangeDto> SubmitCreateAsync(
            Guid productId,
            CreateProductImageRequest request,
            CancellationToken cancellationToken = default
        );

        Task<ProductImageChangeDto?> SubmitUpdateAsync(
            Guid productId,
            Guid imageId,
            UpdateProductImageRequest request,
            CancellationToken cancellationToken = default
        );

        Task<ProductImageChangeDto?> SubmitDeleteAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default
        );

        Task<ProductImageChangeDto?> ApproveChangeAsync(
            Guid productId,
            Guid changeId,
            CancellationToken cancellationToken = default
        );

        Task<ProductImageChangeDto?> RejectChangeAsync(
            Guid productId,
            Guid changeId,
            string reason,
            CancellationToken cancellationToken = default
        );

    }
}
