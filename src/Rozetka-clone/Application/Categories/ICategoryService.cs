using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Categories
{
    public interface ICategoryService
    {
        Task<IReadOnlyList<CategoryTreeDto>> GetTreeAsync(CancellationToken cancellationToken = default);

        Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

        Task<CategoryDto?> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);

        Task<bool> MoveAsync(Guid categoryId, Guid? newParentId, CancellationToken cancellationToken = default);
    }
}
