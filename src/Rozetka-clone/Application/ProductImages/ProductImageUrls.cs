namespace Application.ProductImages;

public static class ProductImageUrls
{
    public static string ForBrowser(Guid productId, Guid imageId, string url) =>
        url.StartsWith("/", StringComparison.Ordinal) && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\')
            ? url
            : $"/uploads/products/{productId:N}/external-{imageId:N}";

    public static string Validate(Guid productId, string value)
    {
        var url = value.Trim();
        var prefix = $"/uploads/products/{productId:N}/";
        if (url.StartsWith(prefix, StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(url[prefix.Length..], @"^[a-fA-F0-9]{32}\.(png|jpe?g|webp|gif)\z",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Host.Length > 0)
            return uri.AbsoluteUri;
        throw new ArgumentException("Use an HTTPS image or an uploaded photo belonging to this product.");
    }

    public static async Task<Dictionary<Guid, string>> PrimaryAsync(Application.Abstractions.IApplicationDbContext db,
        IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToArray();
        var images = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.ProductImages.Where(i => ids.Contains(i.ProductId)).OrderByDescending(i => i.IsMain)
                .ThenBy(i => i.SortOrder).ThenBy(i => i.Id), ct);
        return images.GroupBy(i => i.ProductId).ToDictionary(g => g.Key,
            g => ForBrowser(g.First().ProductId, g.First().Id, g.First().ImageUrl));
    }
}
