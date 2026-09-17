using System.Net.Http.Json;
using AdminPanel.Infrastructure;
using AdminPanel.Services.Abstractions;
using Contracts.Admin.Users;
using Contracts.Common;

namespace AdminPanel.Services.Implementations;

public sealed class UsersApiClient : IUsersApiClient
{
    private readonly HttpClient _httpClient;

    public UsersApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PagedResponse<UserListItemResponse>> GetUsersAsync(
        int page = 1,
        int size = 20,
        string? search = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/admin/users?page={page}&size={size}";

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += $"&query={Uri.EscapeDataString(search)}";
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            url += $"&status={Uri.EscapeDataString(status)}";
        }

        return await _httpClient.GetFromJsonAsync<PagedResponse<UserListItemResponse>>(
                   url,
                   cancellationToken)
               ?? new PagedResponse<UserListItemResponse>([], page, size, 0, 0);
    }

    public async Task<UserDetailsResponse> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/v1/admin/users",
            request,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new ApiException(
                "A user with this email already exists.",
                (int)response.StatusCode,
                "USER_EMAIL_EXISTS");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(
                "The user could not be created.",
                (int)response.StatusCode,
                "USER_CREATE_FAILED");
        }

        return await response.Content.ReadFromJsonAsync<UserDetailsResponse>(
                   cancellationToken: cancellationToken)
               ?? throw new ApiException("The API returned an empty response.", 502);
    }

    public async Task<UserDetailsResponse?> GetUserByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/v1/admin/users/{id}",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDetailsResponse>(
            cancellationToken: cancellationToken);
    }

    public Task<UserStatusResponse> BlockUserAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, "block", cancellationToken);

    public Task<UserStatusResponse> UnblockUserAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, "unblock", cancellationToken);

    private async Task<UserStatusResponse> ChangeStatusAsync(
        Guid id,
        string action,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(
            $"api/v1/admin/users/{id}/{action}",
            content: null,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserStatusResponse>(
                   cancellationToken: cancellationToken)
               ?? throw new ApiException("The API returned an empty response.", 502);
    }
}
