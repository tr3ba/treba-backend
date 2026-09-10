using System.Net.Http.Json;
using AdminPanel.Infrastructure;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public class CategoriesApiClient : ICategoriesApiClient
{
    private readonly HttpClient _httpClient;

    public CategoriesApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<CategoryTreeDto>> GetCategoryTreeAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<CategoryTreeDto>>("api/v1/categories/tree") 
               ?? new List<CategoryTreeDto>();
    }

    public async Task<ApiResponse<CategoryDto>> CreateCategoryAsync(CreateCategoryRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/admin/categories", request);
        return await response.Content.ReadFromJsonAsync<ApiResponse<CategoryDto>>() 
               ?? new ApiResponse<CategoryDto>();
    }

    public async Task<ApiResponse<CategoryDto>> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/v1/admin/categories/{id}", request);
        return await response.Content.ReadFromJsonAsync<ApiResponse<CategoryDto>>() 
               ?? new ApiResponse<CategoryDto>();
    }

    public async Task<ApiResponse<bool>> MoveCategoryAsync(Guid categoryId, Guid newParentId)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/v1/admin/categories/{categoryId}/move", new { newParentId });
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }
}
