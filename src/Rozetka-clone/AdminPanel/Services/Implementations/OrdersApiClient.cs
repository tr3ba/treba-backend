using System.Net.Http.Json;
using AdminPanel.Infrastructure;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public class OrdersApiClient : IOrdersApiClient
{
    private readonly HttpClient _httpClient;

    public OrdersApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PagedResult<OrderSummaryDto>> GetOrdersAsync(int page = 1, int size = 20, string? status = null)
    {
        var url = $"api/v1/admin/orders?page={page}&size={size}";
        if (!string.IsNullOrWhiteSpace(status))
        {
            url += $"&status={Uri.EscapeDataString(status)}";
        }
        return await _httpClient.GetFromJsonAsync<PagedResult<OrderSummaryDto>>(url) ?? new PagedResult<OrderSummaryDto>();
    }

    public async Task<ApiResponse<OrderDto>> GetOrderByIdAsync(Guid id)
    {
        return await _httpClient.GetFromJsonAsync<ApiResponse<OrderDto>>($"api/v1/admin/orders/{id}") 
               ?? new ApiResponse<OrderDto>();
    }

    public async Task<ApiResponse<bool>> UpdateOrderStatusAsync(Guid id, string status)
    {
        var response = await _httpClient.PatchAsJsonAsync($"api/v1/admin/orders/{id}/status", new { status });
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }

    public async Task<ApiResponse<bool>> RefundOrderAsync(Guid orderId, decimal amount)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/v1/admin/orders/{orderId}/refund", new { amount });
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }
}
