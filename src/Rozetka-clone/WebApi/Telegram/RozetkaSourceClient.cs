using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Application.Common;

namespace WebApi.Telegram;

public interface IRozetkaSourceClient
{
    Task<string> GetPageAsync(Uri uri, CancellationToken ct);
    Task<byte[]> GetImageAsync(Uri uri, CancellationToken ct);
}

public sealed class RozetkaSourceClient : IRozetkaSourceClient, IDisposable
{
    private readonly HttpClient client;
    public RozetkaSourceClient()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(8), PooledConnectionLifetime = TimeSpan.FromMinutes(1), ConnectCallback = ConnectPublicAsync };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TrebaCatalogImporter/1.0");
    }
    public async Task<string> GetPageAsync(Uri uri, CancellationToken ct) => Encoding.UTF8.GetString(await DownloadAsync(uri, false, 2 * 1024 * 1024, ct));
    public Task<byte[]> GetImageAsync(Uri uri, CancellationToken ct) => DownloadAsync(uri, true, 5 * 1024 * 1024, ct);

    private async Task<byte[]> DownloadAsync(Uri uri, bool image, int maxBytes, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await DownloadCoreAsync(uri, image, maxBytes, deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new BusinessRuleException("Источник не ответил вовремя. Попробуйте позже или добавьте товар вручную."); }
        catch (HttpRequestException)
        { throw new BusinessRuleException("Не удалось загрузить данные Rozetka. Попробуйте позже или добавьте товар вручную."); }
        catch (IOException)
        { throw new BusinessRuleException("Загрузка данных Rozetka прервалась. Попробуйте ещё раз."); }
    }

    private async Task<byte[]> DownloadCoreAsync(Uri uri, bool image, int maxBytes, CancellationToken ct)
    {
        for (var redirect = 0; redirect < 4; redirect++)
        {
            if (image) ImageUri(uri.AbsoluteUri); else ProductUri(uri.AbsoluteUri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(image ? "image/jpeg, image/png, image/webp, image/gif" : "text/html");
            HttpResponseMessage response;
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            { throw new BusinessRuleException("Не удалось загрузить данные Rozetka. Попробуйте позже или добавьте товар вручную."); }
            using (response)
            {
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (response.Headers.Location is null) break;
                    uri = new Uri(uri, response.Headers.Location); continue;
                }
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                    throw new BusinessRuleException("Rozetka ограничила автоматическую загрузку этой страницы. Попробуйте позже или заполните товар вручную.");
                if (!response.IsSuccessStatusCode) throw new BusinessRuleException("Страница или фотография Rozetka недоступна.");
                var type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (image ? !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) : type is not ("text/html" or "application/xhtml+xml"))
                    throw new BusinessRuleException("Источник вернул неподдерживаемый формат данных.");
                if (response.Content.Headers.ContentLength > maxBytes) throw new BusinessRuleException("Данные источника превышают допустимый размер.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream(); var chunk = new byte[16384]; int count;
                while ((count = await stream.ReadAsync(chunk, ct)) != 0)
                {
                    if (buffer.Length + count > maxBytes) throw new BusinessRuleException("Данные источника превышают допустимый размер.");
                    buffer.Write(chunk, 0, count);
                }
                return buffer.ToArray();
            }
        }
        throw new BusinessRuleException("Слишком много перенаправлений источника.");
    }

    public static Uri ProductUri(string value)
    {
        var uri = SecureUri(value);
        if (!(uri.Host.Equals("rozetka.com.ua", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".rozetka.com.ua", StringComparison.OrdinalIgnoreCase))
            || !Regex.IsMatch(uri.AbsolutePath, @"/p[1-9][0-9]{0,11}/?$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Укажите HTTPS-ссылку Rozetka на конкретный товар, содержащую /p123456/.");
        return new Uri(uri.GetLeftPart(UriPartial.Path));
    }
    public static Uri ImageUri(string value)
    {
        var uri = SecureUri(value);
        if (!Regex.IsMatch(uri.Host, @"^content[0-9]*\.rozetka\.com\.ua$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || !uri.AbsolutePath.StartsWith("/goods/images/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Фотография должна находиться в каталоге изображений Rozetka.");
        return uri;
    }
    private static Uri SecureUri(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType != UriHostNameType.Dns)
            throw new ArgumentException("Разрешены только публичные HTTPS-ссылки Rozetka.");
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
}
