using System.Net.Http.Json;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public sealed class SellersApiClient(HttpClient httpClient) : ISellersApiClient
{
    private const string SellersUrl = "api/v1/admin/sellers";

    public async Task<IReadOnlyList<SellerDto>> GetSellersAsync() =>
        await httpClient.GetFromJsonAsync<List<SellerDto>>(SellersUrl) ?? [];

    public async Task<SellerDto?> GetSellerByIdAsync(Guid id)
    {
        using var response = await httpClient.GetAsync($"{SellersUrl}/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await CatalogApiClient.RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<SellerDto>();
    }

    public Task<SellerDto> ApproveAsync(Guid id) => ChangeStatusAsync(id, "approve");
    public Task<SellerDto> SuspendAsync(Guid id) => ChangeStatusAsync(id, "suspend");

    private async Task<SellerDto> ChangeStatusAsync(Guid id, string action)
    {
        using var response = await httpClient.PostAsync($"{SellersUrl}/{id}/{action}", null);
        await CatalogApiClient.RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<SellerDto>()
            ?? throw new HttpRequestException("API returned an empty response.");
    }
}
