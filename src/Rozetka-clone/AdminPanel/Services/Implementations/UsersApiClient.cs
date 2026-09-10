using System.Net.Http.Json;
using AdminPanel.Infrastructure;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public class UsersApiClient : IUsersApiClient
{
    private readonly HttpClient _httpClient;

    public UsersApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PagedResult<UserDto>> GetUsersAsync(int page = 1, int size = 20, string? search = null)
    {
        var url = $"api/v1/admin/users?page={page}&size={size}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            url += $"&query={Uri.EscapeDataString(search)}";
        }
        return await _httpClient.GetFromJsonAsync<PagedResult<UserDto>>(url) ?? new PagedResult<UserDto>();
    }

    public async Task<ApiResponse<UserDto>> GetUserByIdAsync(Guid id)
    {
        return await _httpClient.GetFromJsonAsync<ApiResponse<UserDto>>($"api/v1/admin/users/{id}") 
               ?? new ApiResponse<UserDto>();
    }

    public async Task<ApiResponse<bool>> BlockUserAsync(Guid id)
    {
        var response = await _httpClient.PostAsync($"api/v1/admin/users/{id}/block", null);
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }

    public async Task<ApiResponse<bool>> UnblockUserAsync(Guid id)
    {
        var response = await _httpClient.PostAsync($"api/v1/admin/users/{id}/unblock", null);
        return await response.Content.ReadFromJsonAsync<ApiResponse<bool>>() 
               ?? new ApiResponse<bool>();
    }
}
