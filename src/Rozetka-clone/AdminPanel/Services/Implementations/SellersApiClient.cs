using System.Net.Http.Json;
using AdminPanel.Infrastructure;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public class SellersApiClient : ISellersApiClient
{
    private readonly HttpClient _httpClient;

    public SellersApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PagedResult<SellerDto>> GetSellersAsync(int page = 1, int size = 20, string? status = null)
    {
        var url = $"api/v1/admin/sellers?page={page}&size={size}";
        if (!string.IsNullOrWhiteSpace(status))
        {
            url += $"&status={Uri.EscapeDataString(status)}";
        }
        return await _httpClient.GetFromJsonAsync<PagedResult<SellerDto>>(url) ?? new PagedResult<SellerDto>();
    }

    public async Task<ApiResponse<SellerDto>> GetSellerByIdAsync(Guid id)
    {
        return await _httpClient.GetFromJsonAsync<ApiResponse<SellerDto>>($"api/v1/admin/sellers/{id}") 
               ?? new ApiResponse<SellerDto>();
    }

    public async Task<ApiResponse<bool>> ApproveSellerAsync(Guid id)
    {
        var response = await _httpClient.PostAsync($"api/v1/admin/sellers/{id}/approve", null);
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }

    public async Task<ApiResponse<bool>> SuspendSellerAsync(Guid id)
    {
        var response = await _httpClient.PostAsync($"api/v1/admin/sellers/{id}/suspend", null);
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }
}
