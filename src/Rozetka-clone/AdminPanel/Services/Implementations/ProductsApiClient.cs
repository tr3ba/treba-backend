using System.Net.Http.Json;
using AdminPanel.Infrastructure;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public class ProductsApiClient : IProductsApiClient
{
    private readonly HttpClient _httpClient;

    public ProductsApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PagedResult<ProductListItemDto>> GetPendingProductsAsync(int page = 1, int size = 20)
    {
        return await _httpClient.GetFromJsonAsync<PagedResult<ProductListItemDto>>($"api/v1/admin/products/pending?page={page}&size={size}") 
               ?? new PagedResult<ProductListItemDto>();
    }

    public async Task<ApiResponse<bool>> ApproveProductAsync(Guid id)
    {
        var response = await _httpClient.PostAsync($"api/v1/admin/products/{id}/approve", null);
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }

    public async Task<ApiResponse<bool>> RejectProductAsync(Guid id, string reason)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/v1/admin/products/{id}/reject", new { reason });
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }
}
