using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductAttributeValues
{
    public interface IProductAttributeValueService
    {
        Task<IReadOnlyList<ProductAttributeValueDto>> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);

        Task<ProductAttributeValueDto?> GetByIdAsync(Guid productId, Guid valueId, CancellationToken cancellationToken = default);

        Task<ProductAttributeValueDto> CreateAsync(Guid productId, CreateProductAttributeValueRequest request, CancellationToken cancellationToken = default);

        Task<ProductAttributeValueDto?> UpdateAsync(Guid productId, Guid valueId, UpdateProductAttributeValueRequest request, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(Guid productId, Guid valueId, CancellationToken cancellationToken = default);
    }
}
