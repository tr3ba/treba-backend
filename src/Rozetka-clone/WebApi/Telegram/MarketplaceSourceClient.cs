using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Application.Common;

namespace WebApi.Telegram;

public interface IMarketplaceSourceClient
{
    Task<string> GetPageAsync(Uri uri, CancellationToken ct);
    Task<byte[]> GetImageAsync(Uri uri, CancellationToken ct);
}

public sealed class MarketplaceSourceClient : IMarketplaceSourceClient, IDisposable
{
    private readonly HttpClient client;
    public MarketplaceSourceClient()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(8), PooledConnectionLifetime = TimeSpan.FromMinutes(1), ConnectCallback = ConnectPublicAsync };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TrebaCatalogImporter/1.0");
    }
    public MarketplaceSourceClient(HttpClient client) => this.client = client;
    public async Task<string> GetPageAsync(Uri uri, CancellationToken ct)
    {
        uri = ProductUri(uri.AbsoluteUri);
        if (Provider(uri) == "rozetka")
        {
            try
            {
                var id = Regex.Match(uri.AbsolutePath, @"/p([0-9]+)/?$").Groups[1].Value;
                var json = JsonNode.Parse(await DownloadAsync(RozetkaApiUri("get-main", id), "api", 2 * 1024 * 1024, ct)) as JsonObject;
                if (!Successful(json) || json!["data"] is not JsonObject)
                    throw new BusinessRuleException("Rozetka не повернула дані товару.");
                try
                {
                    var details = JsonNode.Parse(await DownloadAsync(RozetkaApiUri("get-characteristic", id), "api", 2 * 1024 * 1024, ct));
                    if (Successful(details)) json["trebaCharacteristics"] = details!["data"]?.DeepClone();
                }
                catch (Exception ex) when (ex is BusinessRuleException or System.Text.Json.JsonException) { /* Main product data remains usable. */ }
                if (json["data"] is JsonObject product && MissingDescription(product))
                {
                    using var enrichment = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    enrichment.CancelAfter(TimeSpan.FromSeconds(5));
                    try { json["trebaHtml"] = Encoding.UTF8.GetString(await DownloadAsync(uri, "page", 4 * 1024 * 1024, enrichment.Token)); }
                    catch (Exception ex) when (ex is BusinessRuleException || ex is OperationCanceledException && !ct.IsCancellationRequested)
                    { /* Keep the public API data when the optional description page is blocked. */ }
                }
                return json.ToJsonString();
            }
            catch (Exception ex) when (ex is BusinessRuleException or System.Text.Json.JsonException)
            { /* Some products are available only through their public HTML page. */ }
        }
        return Encoding.UTF8.GetString(await DownloadAsync(uri, "page", 4 * 1024 * 1024, ct));
    }
    public Task<byte[]> GetImageAsync(Uri uri, CancellationToken ct) => DownloadAsync(uri, "image", 5 * 1024 * 1024, ct);
    private static bool Successful(JsonNode? node) => node is JsonObject && node["success"] is JsonValue value && value.TryGetValue<bool>(out var success) && success;
    private static bool MissingDescription(JsonObject product) => string.IsNullOrWhiteSpace(product["description"] is JsonObject description ? description["text"]?.ToString() : product["description"]?.ToString())
        && string.IsNullOrWhiteSpace(product["docket"]?.ToString());
    private static Uri RozetkaApiUri(string action, string id) => new($"https://product-api.rozetka.com.ua/v4/goods/{action}?goodsId={id}&lang=ua&country=UA");

    private async Task<byte[]> DownloadAsync(Uri uri, string kind, int maxBytes, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await DownloadCoreAsync(uri, kind, maxBytes, deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new BusinessRuleException("Джерело не відповіло вчасно. Спробуйте пізніше або додайте товар вручну."); }
        catch (HttpRequestException)
        { throw new BusinessRuleException("Не вдалося завантажити дані маркетплейсу. Спробуйте пізніше або додайте товар вручну."); }
        catch (IOException)
        { throw new BusinessRuleException("Завантаження даних маркетплейсу перервано. Спробуйте ще раз."); }
    }

    private async Task<byte[]> DownloadCoreAsync(Uri uri, string kind, int maxBytes, CancellationToken ct)
    {
        for (var redirect = 0; redirect < 4; redirect++)
        {
            if (kind == "image") ImageUri(uri.AbsoluteUri, allowExternal: true);
            else if (kind == "page") ProductUri(uri.AbsoluteUri);
            else if (uri.Host != "product-api.rozetka.com.ua" || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
                || !Regex.IsMatch(uri.PathAndQuery, @"^/v4/goods/(get-main|get-characteristic)\?goodsId=[1-9][0-9]{0,11}&lang=ua&country=UA$"))
                throw new ArgumentException("Неприпустима адреса API джерела.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.AcceptLanguage.ParseAdd("uk-UA, uk;q=0.9, en;q=0.5");
            request.Headers.Accept.ParseAdd(kind == "image" ? "image/jpeg, image/png, image/webp, image/gif" : kind == "api" ? "application/json" : "text/html");
            HttpResponseMessage response;
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            { throw new BusinessRuleException("Не вдалося завантажити дані маркетплейсу. Спробуйте пізніше або додайте товар вручну."); }
            using (response)
            {
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (response.Headers.Location is null) break;
                    var next = new Uri(uri, response.Headers.Location);
                    if (kind == "page")
                    {
                        var target = ProductUri(next.AbsoluteUri);
                        if (Provider(target) != Provider(uri) || Provider(uri) == "web" &&
                            !SiteHost(target).Equals(SiteHost(uri), StringComparison.OrdinalIgnoreCase))
                            throw new ArgumentException("Джерело перенаправило на інший сайт. Використайте кінцеве посилання на товар.");
                    }
                    uri = next; continue;
                }
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                    throw new BusinessRuleException("Маркетплейс обмежив автоматичне завантаження цієї сторінки. Спробуйте пізніше або заповніть товар вручну.");
                if (!response.IsSuccessStatusCode) throw new BusinessRuleException("Сторінка або фотографія маркетплейсу недоступна.");
                var type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (kind == "image" ? !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) : kind == "api" ? type != "application/json" : type is not ("text/html" or "application/xhtml+xml"))
                    throw new BusinessRuleException("Джерело повернуло непідтримуваний формат даних.");
                if (response.Content.Headers.ContentLength > maxBytes) throw new BusinessRuleException("Дані джерела перевищують допустимий розмір.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream(); var chunk = new byte[16384]; int count;
                while ((count = await stream.ReadAsync(chunk, ct)) != 0)
                {
                    if (buffer.Length + count > maxBytes) throw new BusinessRuleException("Дані джерела перевищують допустимий розмір.");
                    buffer.Write(chunk, 0, count);
                }
                return buffer.ToArray();
            }
        }
        throw new BusinessRuleException("Забагато перенаправлень джерела.");
    }

    public static Uri ProductUri(string value)
    {
        var uri = SecureUri(value);
        var valid = Provider(uri) switch
        {
            "rozetka" => Regex.IsMatch(uri.AbsolutePath, @"/p[1-9][0-9]{0,11}/?$"),
            "prom" => Regex.IsMatch(uri.AbsolutePath, @"^/(?:ua/)?p[1-9][0-9]{0,11}-[^/]+\.html$"),
            "epic" => Regex.IsMatch(uri.AbsolutePath, @"^/(?:ua/)?shop/[^/]+\.html$"),
            "web" => uri.AbsolutePath != "/" || uri.Query.Length > 1,
            _ => false
        };
        if (!valid) throw new ArgumentException("Укажіть публічне HTTPS-посилання на конкретний товар.");
        if (Provider(uri) == "web")
        {
            var builder = new UriBuilder(uri) { Fragment = "" };
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !Regex.IsMatch(part.Split('=')[0], @"^(utm_[^=]*|gclid|fbclid|yclid)$", RegexOptions.IgnoreCase));
            builder.Query = string.Join('&', query);
            return builder.Uri;
        }
        var path = uri.AbsolutePath;
        if (!path.StartsWith("/ua/", StringComparison.OrdinalIgnoreCase)) path = "/ua" + path;
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + path);
    }
    public static string Provider(Uri uri) => uri.Host == "rozetka.com.ua" || uri.Host.EndsWith(".rozetka.com.ua", StringComparison.OrdinalIgnoreCase) ? "rozetka"
        : uri.Host == "prom.ua" || uri.Host.EndsWith(".prom.ua", StringComparison.OrdinalIgnoreCase) ? "prom"
        : uri.Host is "epicentrk.ua" or "www.epicentrk.ua" ? "epic" : "web";
    public static Uri ImageUri(string value, bool allowExternal = false)
    {
        var uri = SecureUri(value);
        var allowed = Regex.IsMatch(uri.Host, @"^content[0-9]*\.rozetka\.com\.ua$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && uri.AbsolutePath.StartsWith("/goods/images/", StringComparison.OrdinalIgnoreCase)
            || uri.Host is "images.prom.ua" or "images.promcdn.ua"
            || uri.Host is "cdn.epicentrk.ua" or "cdn.27.ua" || uri.Host == "epicentrk.ua" && uri.AbsolutePath.StartsWith("/upload/", StringComparison.OrdinalIgnoreCase);
        if (!allowed && !allowExternal) throw new ArgumentException("Фотографія має бути у сховищі підтримуваного маркетплейсу.");
        return uri;
    }
    private static Uri SecureUri(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType != UriHostNameType.Dns)
            throw new ArgumentException("Дозволено лише публічні HTTPS-посилання без пароля та нестандартного порту.");
        var host = uri.IdnHost.TrimEnd('.');
        if (!host.Contains('.') || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || new[] { ".localhost", ".local", ".internal", ".lan", ".home", ".onion", ".arpa" }.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Локальні та внутрішні адреси не підтримуються.");
        return uri;
    }
    public static bool IsPublicAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        var b = ip.GetAddressBytes();
        if (b.Length == 4)
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224 || b[0] == 169 && b[1] == 254
                || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168
                || b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] == 198 && b[1] is 18 or 19
                || b[0] == 192 && b[1] == 0 && b[2] is 0 or 2 || b[0] == 198 && b[1] == 51 && b[2] == 100
                || b[0] == 203 && b[1] == 0 && b[2] == 113);
        return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x02)
            && !(b[0] == 0x20 && b[1] == 0x01 && (b[2] == 0 && b[3] == 0 || b[2] == 0x0d && b[3] == 0xb8));
    }
    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a))) throw new HttpRequestException("Source resolved to an unavailable network.");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("Could not connect to source.");
    }
    public void Dispose() => client.Dispose();
    private static string SiteHost(Uri uri) => uri.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.IdnHost[4..] : uri.IdnHost;
}
