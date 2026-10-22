using System.Net.Http.Json;
using Contracts.Admin.Settings;
using Contracts.Common;

namespace AdminPanel.Services.Implementations;

public sealed class SettingsApiClient(HttpClient http, UiTime time)
{
    public async Task<IReadOnlyList<FeatureFlagDto>> Features()
    {
        using var response = await http.GetAsync("api/v1/admin/settings/features");
        await CatalogApiClient.RequireSuccess(response);
        return (await response.Content.ReadFromJsonAsync<List<FeatureFlagDto>>())!;
    }
    public async Task<FeatureFlagDto> SetFeature(string key, SetFeatureFlagRequest request)
    {
        using var response = await http.PutAsJsonAsync("api/v1/admin/settings/features/" + Uri.EscapeDataString(key), request);
        await CatalogApiClient.RequireSuccess(response);
        return (await response.Content.ReadFromJsonAsync<FeatureFlagDto>())!;
    }
    private static string Encode(string? text) => Uri.EscapeDataString(text ?? "");
    public async Task<PagedResponse<StaffDto>> Staff(int page, string? search, string? role)
    {
        using var response = await http.GetAsync($"api/v1/admin/settings/staff?page={page}&search={Encode(search)}&role={Encode(role)}");
        await CatalogApiClient.RequireSuccess(response);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<StaffDto>>())!;
    }
    public async Task Create(CreateStaffRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/v1/admin/settings/staff", request);
        await CatalogApiClient.RequireSuccess(response);
    }
    public async Task Update(Guid id, UpdateStaffAccessRequest request)
    {
        using var response = await http.PutAsJsonAsync($"api/v1/admin/settings/staff/{id}/access", request);
        await CatalogApiClient.RequireSuccess(response);
    }
    public async Task<PagedResponse<AuditLogDto>> Audit(int page, string? search, string? entityType, string? action, DateOnly? from, DateOnly? to)
    {
        var dates = (from.HasValue ? "&from=" + Encode(time.UtcStart(from.Value).ToString("O")) : "")
            + (to.HasValue ? "&to=" + Encode(time.UtcStart(to.Value.AddDays(1)).ToString("O")) : "");
        using var response = await http.GetAsync($"api/v1/admin/settings/audit?page={page}&search={Encode(search)}&entityType={Encode(entityType)}&action={Encode(action)}{dates}");
        await CatalogApiClient.RequireSuccess(response);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<AuditLogDto>>())!;
    }
}
