using System.Net.Http.Json;
using Contracts.Admin.Dashboard;

namespace AdminPanel.Services.Implementations;

public sealed class DashboardApiClient(HttpClient http)
{
    public async Task<AdminDashboardDto> GetAsync(int days = 30)
    {
        using var response = await http.GetAsync($"api/v1/admin/dashboard?days={days}");
        await CatalogApiClient.RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<AdminDashboardDto>()
            ?? throw new HttpRequestException("API returned an empty response.");
    }
}
