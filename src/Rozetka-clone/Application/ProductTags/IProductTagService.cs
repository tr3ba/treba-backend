using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductTags
{
    public interface IProductTagService
    {
        Task<IReadOnlyList<ProductTagDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<ProductTagDto?> GetByIdAsync(Guid tagId, CancellationToken cancellationToken = default);

        Task<ProductTagDto> CreateAsync(CreateProductTagRequest request, CancellationToken cancellationToken = default);

        Task<ProductTagDto?> UpdateAsync(Guid tagId, UpdateProductTagRequest request, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(Guid tagId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ProductTagDto>> GetProductTagsAsync(Guid productId, CancellationToken cancellationToken = default);

        Task<bool> AddTagToProductAsync(Guid productId, Guid tagId, CancellationToken cancellationToken = default);

        Task<bool> RemoveTagFromProductAsync(Guid productId, Guid tagId, CancellationToken cancellationToken = default);
    }
}
