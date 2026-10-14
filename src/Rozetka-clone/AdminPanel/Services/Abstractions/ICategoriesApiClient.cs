using System.Text.Json.Serialization;
using AdminPanel.Infrastructure;

namespace AdminPanel.Services.Abstractions;

public interface ICategoriesApiClient
{
    Task<List<CategoryTreeDto>> GetCategoryTreeAsync();
    Task<ApiResponse<CategoryDto>> CreateCategoryAsync(CreateCategoryRequest request);
    Task<ApiResponse<CategoryDto>> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request);
    Task<ApiResponse<bool>> MoveCategoryAsync(Guid categoryId, Guid newParentId);
}

public record CategoryDto(Guid Id, Guid? ParentId, string Name, string Slug, string? Description, string? ImageUrl, [property: JsonPropertyName("isActive")] bool Active, int SortOrder, int Level);
public record CategoryTreeDto(Guid Id, string Name, string Slug, [property: JsonPropertyName("isActive")] bool Active, List<CategoryTreeDto> Children, int SortOrder = 0, int Level = 0, string? Description = null);
public record CreateCategoryRequest(Guid? ParentId, string Name, string Slug, string? Description, string? ImageUrl, [property: JsonPropertyName("isActive")] bool Active, int SortOrder);
public record UpdateCategoryRequest(string Name, string Slug, string? Description, string? ImageUrl, [property: JsonPropertyName("isActive")] bool Active, int SortOrder);
