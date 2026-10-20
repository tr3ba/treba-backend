using System.Net.Http.Json;
using System.Text.Json;
using Contracts.Catalog;
using Contracts.Telegram;

namespace AdminPanel.Services.Implementations;

public sealed class SellerImportsApiClient(HttpClient http)
{
    private const string Root = "api/v1/seller/imports/";
    public Task<SellerImportOptionsDto> Options(CancellationToken ct = default) => Get<SellerImportOptionsDto>("options", ct);
    public Task<List<ImportJobDto>> Jobs(CancellationToken ct = default) => Get<List<ImportJobDto>>("queue", ct);
    public Task<ImportPreviewDto> Preview(Guid id, CancellationToken ct = default) => Get<ImportPreviewDto>(id.ToString(), ct);
    public Task<List<ImportJobDto>> Add(QueueImportsRequest request, CancellationToken ct = default) => Post<List<ImportJobDto>>("queue", request, ct);
    public Task<CatalogLookupDto> CreateBrand(Guid id, string name, CancellationToken ct = default) => Post<CatalogLookupDto>($"{id}/brand", new CreateImportBrandRequest(name), ct);
    public async Task Retry(Guid id, CancellationToken ct = default)
    { using var response = await http.PostAsync(Root + $"queue/{id}/retry", null, ct); await CatalogApiClient.RequireSuccess(response); }
    public async Task<Guid> Submit(Guid id, SubmitImportRequest request, CancellationToken ct = default)
    { var result = await Post<JsonElement>($"{id}/submit", request, ct); return result.GetProperty("productId").GetGuid(); }
    private async Task<T> Get<T>(string path, CancellationToken ct)
    { using var response = await http.GetAsync(Root + path, ct); await CatalogApiClient.RequireSuccess(response); return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!; }
    private async Task<T> Post<T>(string path, object body, CancellationToken ct)
    { using var response = await http.PostAsJsonAsync(Root + path, body, ct); await CatalogApiClient.RequireSuccess(response); return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!; }
}
