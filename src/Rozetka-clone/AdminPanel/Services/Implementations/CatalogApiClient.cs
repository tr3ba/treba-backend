using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Contracts.Catalog;
using Contracts.Common;

namespace AdminPanel.Services.Implementations;

public sealed class CatalogApiClient(HttpClient http)
{
    public Task<PagedResponse<CatalogProductDto>> List(int page = 1, string? search = null, string? status = null) =>
        Get<PagedResponse<CatalogProductDto>>($"api/v1/admin/catalog/products?page={page}&size=12&search={Uri.EscapeDataString(search ?? "")}&status={Uri.EscapeDataString(status ?? "")}");
    public Task<CatalogStatsDto> Stats() => Get<CatalogStatsDto>("api/v1/admin/catalog/stats");
    public Task<int> PendingCount() => Get<int>("api/v1/admin/catalog/moderation/pending-count");
    public Task<CatalogProductDto> GetProduct(Guid id) => Get<CatalogProductDto>($"api/v1/admin/catalog/products/{id}");
    public Task<List<CatalogLookupDto>> Categories() => Get<List<CatalogLookupDto>>("api/v1/admin/catalog/categories");
    public Task<List<CatalogLookupDto>> Brands() => Get<List<CatalogLookupDto>>("api/v1/admin/catalog/brands");
    public Task<List<CatalogLookupDto>> Stores() => Get<List<CatalogLookupDto>>("api/v1/admin/catalog/stores");
    public Task<List<CatalogProductImageDto>> Images(Guid productId) =>
        Get<List<CatalogProductImageDto>>($"api/v1/products/{productId}/images");

    public string ResolveImageUrl(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        return new Uri(
            http.BaseAddress ?? throw new InvalidOperationException("API base address is missing."),
            imageUrl.TrimStart('/')).ToString();
    }

    public async Task<CatalogProductImageDto> UploadImage(
        Guid productId,
        Stream content,
        string fileName,
        string contentType,
        bool isMain,
        int sortOrder)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(isMain.ToString()), "isMain");
        form.Add(new StringContent(sortOrder.ToString(System.Globalization.CultureInfo.InvariantCulture)), "sortOrder");
        using var response = await http.PostAsync($"api/v1/products/{productId}/images/upload", form);
        await RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<CatalogProductImageDto>()
            ?? throw new HttpRequestException("API returned an empty response.");
    }

    public async Task DeleteImage(Guid productId, Guid imageId)
    {
        using var response = await http.DeleteAsync($"api/v1/products/{productId}/images/{imageId}");
        await RequireSuccess(response);
    }

    public async Task<string> ImageDataUrl(string imageUrl)
    {
        using var response = await http.GetAsync(imageUrl.TrimStart('/'));
        await RequireSuccess(response);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        return $"data:{mediaType};base64,{Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync())}";
    }
    public async Task<CatalogProductDto> Save(Guid? id, SaveCatalogProductRequest request)
    {
        using var response = id.HasValue ? await http.PutAsJsonAsync($"api/v1/admin/catalog/products/{id}", request) : await http.PostAsJsonAsync("api/v1/admin/catalog/products", request);
        await RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<CatalogProductDto>() ?? throw new HttpRequestException("API returned an empty response.");
    }
    public async Task Action(Guid id, string action)
    {
        var prefix = action == "submit" ? "seller" : "admin";
        using var response = await http.PostAsync($"api/v1/{prefix}/products/{id}/{action}", null);
        await RequireSuccess(response);
    }
    private async Task<T> Get<T>(string url)
    {
        using var response = await http.GetAsync(url);
        await RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new HttpRequestException("API returned an empty response.");
    }
    internal static async Task RequireSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Сеанс завершився. Увійдіть знову.",
            System.Net.HttpStatusCode.Forbidden => "Недостатньо прав доступу.",
            _ => "Запит не виконано. Спробуйте ще раз."
        };
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (body.RootElement.TryGetProperty("title", out var title)) message = title.GetString() ?? message;
        }
        catch (JsonException) { }
        throw new HttpRequestException(message, null, response.StatusCode);
    }
}
