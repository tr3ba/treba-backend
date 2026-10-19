using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Application.Common;
using Contracts.Telegram;

namespace WebApi.Telegram;

public sealed record RozetkaProductData(string Id, string Url, string Name, string Description, string BrandName,
    string CategoryKey, string CategoryName, decimal? Price, IReadOnlyList<string> Images, IReadOnlyList<ImportedCharacteristicDto> Characteristics);

public sealed class RozetkaProductParser
{
    public RozetkaProductData Parse(Uri source, string html)
    {
        source = RozetkaSourceClient.ProductUri(source.AbsoluteUri);
        if (html.Length > 2 * 1024 * 1024) throw new ArgumentException("Страница слишком большая.");
        using var document = new HtmlParser().ParseDocument(html);
        if (document.QuerySelector("#challenge-running, #challenge-form, script[src*='challenge-platform']") is not null
            || document.Title?.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) == true)
            throw new BusinessRuleException("Rozetka ограничила автоматическую загрузку. Попробуйте позже или добавьте товар вручную.");
        var documents = new List<JsonDocument>();
        try
        {
            foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
                try { documents.Add(JsonDocument.Parse(script.TextContent, new JsonDocumentOptions { MaxDepth = 48 })); } catch (JsonException) { }
            var nodes = documents.SelectMany(d => Walk(d.RootElement)).ToList();
            var id = Regex.Match(source.AbsolutePath, @"/p([0-9]+)/?$").Groups[1].Value;
            var products = nodes.Where(n => Type(n, "Product")).ToList();
            var product = products.FirstOrDefault(p => Text(p, "sku") == id || Text(p, "url")?.Contains("/p" + id, StringComparison.Ordinal) == true);
            if (product.ValueKind == JsonValueKind.Undefined) product = products.FirstOrDefault();
            var name = Clean(Text(product, "name") ?? Meta(document, "og:title") ?? document.QuerySelector("h1")?.TextContent ?? "", 250);
            if (string.IsNullOrWhiteSpace(name)) throw new BusinessRuleException("Не удалось прочитать название товара Rozetka.");
            var categoryKey = ""; var categoryName = "";
            foreach (var crumb in nodes.Where(n => Type(n, "BreadcrumbList")))
                if (crumb.TryGetProperty("itemListElement", out var items) && items.ValueKind == JsonValueKind.Array)
                    foreach (var item in items.EnumerateArray())
                    {
                        var link = item.TryGetProperty("item", out var value) ? value : default;
                        var url = link.ValueKind == JsonValueKind.String ? link.GetString() : Text(link, "@id") ?? Text(link, "url");
                        var match = Regex.Match(url ?? "", @"/c([1-9][0-9]{0,11})(?:/|$)");
                        if (match.Success) { categoryKey = "c" + match.Groups[1].Value; categoryName = Clean(Text(item, "name") ?? Text(link, "name") ?? "", 250); }
                    }
            if (categoryKey.Length == 0)
                foreach (var link in document.QuerySelectorAll("[itemtype*='BreadcrumbList'] a[href], .breadcrumbs a[href], a.breadcrumbs__link[href]"))
                {
                    var match = Regex.Match(link.GetAttribute("href") ?? "", @"/c([1-9][0-9]{0,11})(?:/|$)");
                    if (match.Success) { categoryKey = "c" + match.Groups[1].Value; categoryName = Clean(link.TextContent, 250); }
                }
            if (categoryKey.Length == 0) throw new BusinessRuleException("Не удалось определить категорию источника. Импорт без проверки категории недоступен.");
            var description = Text(product, "description") ?? document.QuerySelector(".product-about__description, [itemprop='description']")?.InnerHtml ?? Meta(document, "og:description") ?? "";
            var brand = product.ValueKind == JsonValueKind.Object && product.TryGetProperty("brand", out var brandNode)
                ? brandNode.ValueKind == JsonValueKind.String ? brandNode.GetString() : Text(brandNode, "name") : "";
            var images = new List<string>();
            if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("image", out var imageNode))
                foreach (var image in imageNode.ValueKind == JsonValueKind.Array ? imageNode.EnumerateArray().ToArray() : [imageNode])
                    AddImage(images, image.ValueKind == JsonValueKind.String ? image.GetString() : Text(image, "url") ?? Text(image, "contentUrl"));
            if (images.Count == 0) AddImage(images, Meta(document, "og:image"));
            var characteristics = new List<ImportedCharacteristicDto>();
            if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("additionalProperty", out var properties) && properties.ValueKind == JsonValueKind.Array)
                foreach (var property in properties.EnumerateArray().Take(100))
                {
                    var key = Clean(Text(property, "name") ?? "", 200); var value = Clean(Text(property, "value") ?? "", 2000);
                    if (key.Length > 0 && value.Length > 0) characteristics.Add(new(key, value));
                }
            decimal? price = null;
            if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("offers", out var offers))
                foreach (var offer in offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().ToArray() : [offers])
                    if (Text(offer, "priceCurrency") is null or "UAH" && decimal.TryParse(Text(offer, "price") ?? Text(offer, "lowPrice"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and <= 999999999.99m)
                    { price = decimal.Round(parsed, 2); break; }
            return new(id, source.AbsoluteUri, name, Clean(description, 10000), Clean(brand ?? "", 200), categoryKey, categoryName, price, images.Take(8).ToList(), characteristics);
        }
        finally { foreach (var json in documents) json.Dispose(); }
    }
    private static void AddImage(List<string> images, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || images.Count >= 8) return;
        try { var uri = RozetkaSourceClient.ImageUri(value); if (!images.Contains(uri.AbsoluteUri, StringComparer.Ordinal)) images.Add(uri.AbsoluteUri); }
        catch (ArgumentException) { }
    }
    private static string? Meta(IDocument document, string key) => document.QuerySelector("meta[property='" + key + "']")?.GetAttribute("content");
    private static bool Type(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty("@type", out var type)
        && (type.ValueKind == JsonValueKind.String ? type.GetString() == name : type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == name));
    private static string? Text(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value)
        && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;
    private static IEnumerable<JsonElement> Walk(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object) { yield return node; foreach (var property in node.EnumerateObject()) foreach (var child in Walk(property.Value)) yield return child; }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) foreach (var child in Walk(item)) yield return child;
    }
    public static string Clean(string value, int max)
    {
        using var document = new HtmlParser().ParseDocument("<div>" + value + "</div>");
        foreach (var unsafeNode in document.QuerySelectorAll("script,style,iframe,object,template")) unsafeNode.Remove();
        foreach (var br in document.QuerySelectorAll("br")) br.Replace(document.CreateTextNode("\n"));
        foreach (var paragraph in document.QuerySelectorAll("p,li,h1,h2,h3")) paragraph.AppendChild(document.CreateTextNode("\n"));
        var plain = Regex.Replace(document.Body?.TextContent ?? "", @"[\t\r ]+", " ").Trim();
        return plain[..Math.Min(max, plain.Length)];
    }
}
