using AdminPanel.Infrastructure;

namespace AdminPanel.Services.Abstractions;

public interface ICategoriesApiClient
{
    Task<List<CategoryTreeDto>> GetCategoryTreeAsync();
    Task<ApiResponse<CategoryDto>> CreateCategoryAsync(CreateCategoryRequest request);
    Task<ApiResponse<CategoryDto>> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request);
    Task<ApiResponse<bool>> MoveCategoryAsync(Guid categoryId, Guid newParentId);
}

public record CategoryDto(Guid Id, Guid? ParentId, string Name, string Slug, string? Description, string? ImageUrl, bool Active, int SortOrder, int Level);
public record CategoryTreeDto(Guid Id, string Name, string Slug, bool Active, List<CategoryTreeDto> Children);
public record CreateCategoryRequest(Guid? ParentId, string Name, string Slug, string? Description, string? ImageUrl, bool Active, int SortOrder);
public record UpdateCategoryRequest(string Name, string Slug, string? Description, string? ImageUrl, bool Active, int SortOrder);
