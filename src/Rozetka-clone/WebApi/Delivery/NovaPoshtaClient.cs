using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions;
using Contracts.Delivery;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace WebApi.Delivery;

public sealed class NovaPoshtaOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = "";
    public Guid SenderCityRef { get; set; }
    public decimal DefaultWeightKg { get; set; } = 1;
    public bool Configured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}

// This client exposes directory and calculation methods only. It cannot register a carrier shipment.
public sealed class NovaPoshtaClient(HttpClient http, IOptions<NovaPoshtaOptions> settings, IMemoryCache cache) : ICheckoutDeliveryProvider
{
    private readonly NovaPoshtaOptions options = settings.Value;
    public bool Available => options.Configured;
    public decimal DefaultWeightKg => options.DefaultWeightKg;

    public async Task<IReadOnlyList<DeliveryCity>> CitiesAsync(string query, CancellationToken ct)
    {
        query = Query(query);
        if (query.Length < 2) return [];
        var rows = await CachedAsync("cities:" + query, "Address", "getCities", new { FindByString = query, Limit = "20", Page = "1" }, ct);
        return rows.Select(City).ToArray();
    }

    public async Task<DeliveryPickupPage> PickupPointsAsync(Guid cityRef, string query, string kind, int page, CancellationToken ct)
    {
        if (cityRef == Guid.Empty || page is < 1 or > 100 || kind is not ("all" or "branch" or "postomat"))
            throw new ArgumentException("Некоректний фільтр відділень.");
        query = Query(query);
        var rows = await CachedAsync($"points:{cityRef}:{query}:{page}", "Address", "getWarehouses",
            new { CityRef = cityRef, FindByString = query, Limit = "50", Page = page.ToString(CultureInfo.InvariantCulture), Language = "UA" }, ct);
        return new(rows.Where(Working).Select(Point).Where(p => p.CityRef == cityRef
            && (kind == "all" || p.Postomat == (kind == "postomat"))).ToArray(), page, rows.Length == 50 && page < 100);
    }

    public async Task<(DeliveryCity City, DeliveryPickupPoint PickupPoint, decimal Price)> QuoteAsync(
        Guid cityRef, Guid pickupPointRef, decimal weightKg, decimal declaredValue, CancellationToken ct)
    {
        if (cityRef == Guid.Empty || pickupPointRef == Guid.Empty || weightKg is <= 0 or > 1000 || declaredValue is <= 0 or > 100000000)
            throw new ArgumentException("Некоректні параметри доставки.");
        var cities = await CallAsync("Address", "getCities", new { Ref = cityRef, Limit = "1" }, ct);
        var warehouses = await CallAsync("Address", "getWarehouses", new { Ref = pickupPointRef, CityRef = cityRef, Limit = "1", Language = "UA" }, ct);
        var cityRow = cities.SingleOrDefault(c => Id(c, "Ref") == cityRef);
        var pointRow = warehouses.SingleOrDefault(p => Id(p, "Ref") == pickupPointRef && Id(p, "CityRef") == cityRef && Working(p));
        if (cityRow.ValueKind != JsonValueKind.Object || pointRow.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Відділення не належить вибраному місту або вже недоступне.");
        foreach (var key in new[] { "TotalMaxWeightAllowed", "PlaceMaxWeightAllowed" })
            if (decimal.TryParse(Text(pointRow, key), CultureInfo.InvariantCulture, out var max) && max > 0 && weightKg > max)
                throw new ArgumentException("Вага перевищує обмеження цього відділення. Виберіть інше.");
        var point = Point(pointRow);
        var senderCityRef = options.SenderCityRef;
        if (senderCityRef == Guid.Empty)
        {
            var origins = await CitiesAsync("Київ", ct);
            senderCityRef = origins.FirstOrDefault(c => c.Name == "Київ")?.Ref ?? Guid.Empty;
            if (senderCityRef == Guid.Empty) throw new DeliveryUnavailableException();
        }
        var prices = await CallAsync("InternetDocument", "getDocumentPrice", new
        {
            CitySender = senderCityRef, CityRecipient = cityRef,
            Weight = weightKg.ToString(CultureInfo.InvariantCulture),
            Cost = declaredValue.ToString(CultureInfo.InvariantCulture),
            ServiceType = point.Postomat ? "WarehousePostomat" : "WarehouseWarehouse", CargoType = "Parcel", SeatsAmount = "1"
        }, ct);
        if (prices.Length != 1 || !decimal.TryParse(Text(prices[0], "Cost"), CultureInfo.InvariantCulture, out var price)
            || price is < 0 or > 1000000 || decimal.Round(price, 2) != price)
            throw new DeliveryUnavailableException();
        return (City(cityRow), point, price);
    }

    private async Task<JsonElement[]> CachedAsync(string key, string model, string method, object properties, CancellationToken ct)
    {
        if (!Available) throw new DeliveryUnavailableException();
        if (cache.TryGetValue<JsonElement[]>("np:" + key, out var cached)) return cached!;
        var result = await CallAsync(model, method, properties, ct);
        cache.Set("np:" + key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5), Size = 1 });
        return result;
    }

    private async Task<JsonElement[]> CallAsync(string model, string method, object properties, CancellationToken ct)
    {
        if (!Available) throw new DeliveryUnavailableException();
        try
        {
            using var response = await http.PostAsJsonAsync("https://api.novaposhta.ua/v2.0/json/",
                new { apiKey = options.ApiKey, modelName = model, calledMethod = method, methodProperties = properties }, ct);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new DeliveryUnavailableException();
            return data.EnumerateArray().Select(row => row.Clone()).ToArray();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        { throw new DeliveryUnavailableException(); }
    }

    private static string Query(string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length > 100) throw new ArgumentException("Пошуковий запит занадто довгий.");
        return value;
    }
    private static string Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.ToString() : "";
    private static Guid Id(JsonElement row, string key) => Guid.TryParse(Text(row, key), out var id) ? id : Guid.Empty;
    private static bool Working(JsonElement row) => Text(row, "WarehouseStatus") is "" or "Working";
    private static DeliveryCity City(JsonElement row) => new(Id(row, "Ref"), Text(row, "Description"), Text(row, "AreaDescription"));
    private static DeliveryPickupPoint Point(JsonElement row) => new(Id(row, "Ref"), Id(row, "CityRef"),
        Text(row, "Description"), Text(row, "Number"), Text(row, "CategoryOfWarehouse") == "Postomat"
            || Text(row, "Description").Contains("поштомат", StringComparison.OrdinalIgnoreCase));
}
