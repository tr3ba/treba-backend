using System.Net.Http.Json;
using Contracts.Admin.Reviews;
using Contracts.Common;

namespace AdminPanel.Services.Implementations;

public sealed class ReviewsApiClient(HttpClient http)
{
    public async Task<PagedResponse<AdminReviewDto>> ListAsync(int page = 1, string? status = "PENDING", string? search = null)
    {
        var url = $"api/v1/admin/reviews?page={page}&size=12&status={Uri.EscapeDataString(status ?? "")}&search={Uri.EscapeDataString(search ?? "")}";
        using var response = await http.GetAsync(url);
        await CatalogApiClient.RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<PagedResponse<AdminReviewDto>>()
            ?? throw new HttpRequestException("API returned an empty response.");
    }

    public async Task<int> PendingCountAsync()
    {
        using var response = await http.GetAsync("api/v1/admin/reviews/pending-count");
        await CatalogApiClient.RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<int>();
    }

    public async Task ModerateAsync(Guid id, string status)
    {
        using var response = await http.PutAsJsonAsync($"api/v1/admin/reviews/{id}/moderate", new ModerateReviewRequest(status));
        await CatalogApiClient.RequireSuccess(response);
    }
}
