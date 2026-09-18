using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductImages
{
    public interface IProductImageService
    {
        Task<IReadOnlyList<ProductImageDto>> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);

        Task<ProductImageDto?> GetByIdAsync(Guid productId, Guid imageId, CancellationToken cancellationToken = default);

        Task<ProductImageDto> CreateAsync(Guid productId, CreateProductImageRequest request, CancellationToken cancellationToken = default);

        Task<ProductImageDto?> UpdateAsync(Guid productId, Guid imageId, UpdateProductImageRequest request, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(Guid productId, Guid imageId, CancellationToken cancellationToken = default);
    }
}
