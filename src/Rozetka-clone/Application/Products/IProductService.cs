using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Products
{
    public interface IProductService
    {
        Task<IReadOnlyList<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<ProductDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

        Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

        Task<ProductDto?> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default);

        Task<bool> SubmitForModerationAsync(Guid id, CancellationToken cancellationToken = default);

        Task<bool> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

        Task<bool> RejectAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
