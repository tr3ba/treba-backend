using System.Net.Http.Json;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public sealed class SellersApiClient(HttpClient httpClient) : ISellersApiClient
{
    private const string SellersUrl = "api/v1/admin/sellers";

    public async Task<IReadOnlyList<SellerDto>> GetSellersAsync() =>
        await httpClient.GetFromJsonAsync<List<SellerDto>>(SellersUrl) ?? [];

    // The current API exposes the collection, but no GET by id endpoint.
    public async Task<SellerDto?> GetSellerByIdAsync(Guid id) =>
        (await GetSellersAsync()).FirstOrDefault(seller => seller.Id == id);
}
