using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Contracts.Catalog;
using Contracts.Common;

namespace AdminPanel.Services.Implementations;

public sealed class CatalogApiClient(HttpClient http)
{
    public Task<PagedResponse<CatalogProductDto>> List(int page = 1, string? search = null, string? status = null,
        Guid? categoryId = null, Guid? brandId = null, Guid? sellerId = null, decimal? minPrice = null, decimal? maxPrice = null, string? stock = null, string? sort = null) =>
        Get<PagedResponse<CatalogProductDto>>($"api/v1/admin/catalog/products?page={page}&size=12&search={Encode(search)}&status={Encode(status)}"
            + $"&categoryId={categoryId}&brandId={brandId}&sellerId={sellerId}&minPrice={Number(minPrice)}&maxPrice={Number(maxPrice)}&stock={Encode(stock)}&sort={Encode(sort)}");
    private static string Encode(string? value) => Uri.EscapeDataString(value ?? "");
    private static string Number(decimal? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public Task<CatalogFilterOptionsDto> FilterOptions() => Get<CatalogFilterOptionsDto>("api/v1/admin/catalog/filter-options");
    public Task<List<CatalogAttributeDefinitionDto>> AttributeDefinitions(Guid categoryId) => Get<List<CatalogAttributeDefinitionDto>>($"api/v1/admin/catalog/attributes?categoryId={categoryId}");
    public Task<List<ProductModerationDto>> ModerationHistory(Guid id) => Get<List<ProductModerationDto>>($"api/v1/admin/catalog/products/{id}/moderation-history");
    public async Task<Guid> CreateAttribute(CreateCatalogAttributeRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/v1/admin/catalog/attributes", request);
        await RequireSuccess(response);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return result.RootElement.GetProperty("id").GetGuid();
    }
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

    public async Task<string?> ImageDataUrl(string imageUrl)
    {
        try
        {
            using var response = await http.GetAsync(imageUrl.TrimStart('/'));
            if (!response.IsSuccessStatusCode
                && response.StatusCode is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden))
                return null;

            await RequireSuccess(response);
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length == 0 ? null : $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden))
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }
    public async Task<CatalogProductDto> Save(Guid? id, SaveCatalogProductRequest request)
    {
        using var response = id.HasValue ? await http.PutAsJsonAsync($"api/v1/admin/catalog/products/{id}", request) : await http.PostAsJsonAsync("api/v1/admin/catalog/products", request);
        await RequireSuccess(response);
        return await response.Content.ReadFromJsonAsync<CatalogProductDto>() ?? throw new HttpRequestException("API returned an empty response.");
    }
    public async Task Action(Guid id, string action, string? reason = null)
    {
        var prefix = action == "submit" ? "seller" : "admin";
        var url = $"api/v1/{prefix}/products/{id}/{action}";
        using var response = action == "reject" ? await http.PostAsJsonAsync(url, new RejectProductRequest(reason ?? "")) : await http.PostAsync(url, null);
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
