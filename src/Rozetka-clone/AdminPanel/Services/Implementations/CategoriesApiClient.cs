using System.Net.Http.Json;
using AdminPanel.Infrastructure;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public class CategoriesApiClient(HttpClient http) : ICategoriesApiClient
{
    public async Task<List<CategoryTreeDto>> GetCategoryTreeAsync() => await http.GetFromJsonAsync<List<CategoryTreeDto>>("api/v1/categories") ?? [];
    public async Task<ApiResponse<CategoryDto>> CreateCategoryAsync(CreateCategoryRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/v1/categories", new { request.ParentId, request.Name, request.Slug, request.Description, request.ImageUrl, IsActive=request.Active, request.SortOrder });
        await CatalogApiClient.RequireSuccess(response);
        return new() { Data=await response.Content.ReadFromJsonAsync<CategoryDto>() };
    }
    public async Task<ApiResponse<CategoryDto>> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request)
    {
        using var response = await http.PatchAsJsonAsync($"api/v1/categories/{id}", new { request.Name, request.Slug, request.Description, request.ImageUrl, IsActive=request.Active, request.SortOrder });
        await CatalogApiClient.RequireSuccess(response);
        return new() { Data=await response.Content.ReadFromJsonAsync<CategoryDto>() };
    }
    public async Task<ApiResponse<bool>> MoveCategoryAsync(Guid id, Guid newParentId)
    {
        using var response = await http.PostAsJsonAsync($"api/v1/categories/{id}/move", new { newParentId });
        await CatalogApiClient.RequireSuccess(response);
        return new() { Data=true };
    }
}
