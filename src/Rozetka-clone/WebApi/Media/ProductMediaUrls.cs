using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Infrastructure.Authentication;
using Microsoft.Extensions.Options;

namespace WebApi.Media;

public sealed class ProductMediaUrls(IHttpContextAccessor http, IOptions<SecurityOptions> security, TimeProvider clock) : IProductMediaUrls
{
    public string ForCurrentActor(string url)
    {
        var userId = http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id) || !url.StartsWith("/uploads/products/", StringComparison.Ordinal))
            return url;
        var expiry = clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds();
        var payload = $"{id:N}.{expiry}";
        return url + "?access=" + payload + "." + Signature(url, payload);
    }

    public Guid? Validate(Guid productId, string fileName, string? token)
    {
        if (token is null || token.Length > 200) return null;
        var parts = token.Split('.');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var id)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry)
            || expiry <= clock.GetUtcNow().ToUnixTimeSeconds()
            || expiry > clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds()) return null;
        var expected = Signature($"/uploads/products/{productId:N}/{fileName}", parts[0] + "." + parts[1]);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])) ? id : null;
    }

    private string Signature(string url, string payload) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromBase64String(security.Value.OtpHashKey), Encoding.UTF8.GetBytes("product-media:" + url + ":" + payload)));
}
