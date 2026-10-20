using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Application.Common;
using Contracts.Telegram;

namespace WebApi.Telegram;

public sealed record MarketplaceProductData(string Id, string Url, string Name, string Description, string BrandName,
    string CategoryKey, string CategoryName, decimal? Price, IReadOnlyList<string> Images, IReadOnlyList<ImportedCharacteristicDto> Characteristics);

public sealed class MarketplaceProductParser
{
    public MarketplaceProductData Parse(Uri source, string html)
    {
        source = MarketplaceSourceClient.ProductUri(source.AbsoluteUri);
        if (html.Length > 4 * 1024 * 1024) throw new ArgumentException("Страница слишком большая.");
        var provider = MarketplaceSourceClient.Provider(source);
        if (provider == "rozetka" && html.AsSpan().TrimStart().StartsWith("{")) return ParseRozetkaApi(source, html);
        using var document = new HtmlParser().ParseDocument(html);
        if (document.QuerySelector("#challenge-running, #challenge-form, script[src*='challenge-platform']") is not null
            || document.Title?.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) == true)
            throw new BusinessRuleException("Маркетплейс ограничил автоматическую загрузку. Попробуйте позже или добавьте товар вручную.");
        var documents = new List<JsonDocument>();
        try
        {
            foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
                try { documents.Add(JsonDocument.Parse(script.TextContent, new JsonDocumentOptions { MaxDepth = 48 })); } catch (JsonException) { }
            var nodes = documents.SelectMany(d => Walk(d.RootElement)).ToList();
            JsonElement epicMetadata = default;
            if (provider == "epic")
                foreach (var script in document.QuerySelectorAll("script"))
                {
                    if (script.TextContent.Length > 16384 || !script.TextContent.Contains("dataLayer.push", StringComparison.Ordinal)) continue;
                    var match = Regex.Match(script.TextContent, @"dataLayer\.push\(\s*(\{.*?\})\s*\)", RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
                    if (!match.Success) continue;
                    try
                    {
                        var json = JsonDocument.Parse(match.Groups[1].Value);
                        documents.Add(json);
                        if (Text(json.RootElement, "pageType") == "ProductPage") { epicMetadata = json.RootElement; break; }
                    }
                    catch (JsonException) { }
                }
            var id = provider == "rozetka" ? Regex.Match(source.AbsolutePath, @"/p([0-9]+)/?$").Groups[1].Value
                : provider == "prom" ? "prom:" + Regex.Match(source.AbsolutePath, @"/p([0-9]+)-").Groups[1].Value
                : "epic:" + (Text(epicMetadata, "productId") is { } epicId && Regex.IsMatch(epicId, @"^[0-9]{1,20}$") ? epicId : Hash(source.AbsolutePath.Replace("/ua/", "/", StringComparison.Ordinal)));
            var products = nodes.Where(n => Type(n, "Product")).ToList();
            var product = products.FirstOrDefault(p => Text(p, "sku") == id || Text(p, "url")?.Contains("/p" + id, StringComparison.Ordinal) == true);
            if (product.ValueKind == JsonValueKind.Undefined) product = products.FirstOrDefault();
            if (product.ValueKind == JsonValueKind.Undefined && epicMetadata.ValueKind == JsonValueKind.Undefined
                && document.QuerySelector("[itemtype*='schema.org/Product']") is null && Meta(document, "og:type") is not ("product" or "product.item"))
                throw new BusinessRuleException("Источник не вернул карточку товара.");
            var name = Clean(Text(product, "name") ?? Text(epicMetadata, "productName") ?? document.QuerySelector("h1")?.TextContent ?? Meta(document, "og:title") ?? "", 250);
            if (string.IsNullOrWhiteSpace(name)) throw new BusinessRuleException("Не удалось прочитать название товара.");
            var categoryKey = ""; var categoryName = "";
            foreach (var crumb in nodes.Where(n => Type(n, "BreadcrumbList")))
                if (crumb.TryGetProperty("itemListElement", out var items) && items.ValueKind == JsonValueKind.Array)
                    foreach (var item in items.EnumerateArray())
                    {
                        var link = item.TryGetProperty("item", out var value) ? value : default;
                        var url = link.ValueKind == JsonValueKind.String ? link.GetString() : Text(link, "@id") ?? Text(link, "url");
                        var match = Regex.Match(url ?? "", @"/c([1-9][0-9]{0,11})(?:/|$)");
                        if (provider == "rozetka" && match.Success) { categoryKey = "c" + match.Groups[1].Value; categoryName = Clean(Text(item, "name") ?? Text(link, "name") ?? "", 250); }
                        else if (provider != "rozetka" && Uri.TryCreate(source, url, out var categoryUri) && MarketplaceSourceClient.Provider(categoryUri) == provider
                            && !IsProduct(categoryUri) && categoryUri.AbsolutePath.Trim('/').Length > 2)
                        { categoryKey = provider + ":" + Hash(categoryUri.AbsolutePath.Replace("/ua/", "/", StringComparison.Ordinal)); categoryName = Clean(Text(item, "name") ?? Text(link, "name") ?? "", 250); }
                    }
            if (categoryKey.Length == 0)
                foreach (var link in document.QuerySelectorAll("[itemtype*='BreadcrumbList'] a[href], .breadcrumbs a[href], a.breadcrumbs__link[href]"))
                {
                    var match = Regex.Match(link.GetAttribute("href") ?? "", @"/c([1-9][0-9]{0,11})(?:/|$)");
                    if (provider == "rozetka" && match.Success) { categoryKey = "c" + match.Groups[1].Value; categoryName = Clean(link.TextContent, 250); }
                    else if (provider != "rozetka" && Uri.TryCreate(source, link.GetAttribute("href"), out var categoryUri)
                        && MarketplaceSourceClient.Provider(categoryUri) == provider && !IsProduct(categoryUri) && categoryUri.AbsolutePath.Trim('/').Length > 2)
                    { categoryKey = provider + ":" + Hash(categoryUri.AbsolutePath.Replace("/ua/", "/", StringComparison.Ordinal)); categoryName = Clean(link.TextContent, 250); }
                }
            if (Text(epicMetadata, "categoryId") is { } epicCategory)
            { categoryKey = "epic:" + Hash(epicCategory); categoryName = Clean(Text(epicMetadata, "categoryName") ?? "", 250); }
            if (categoryKey.Length == 0)
            { categoryKey = provider + ":unknown"; categoryName = Clean(Text(product, "category") ?? "Категория источника не указана", 250); }
            var description = Clean(Text(product, "description") ?? Meta(document, "og:description") ?? "", 10000);
            var pageDescription = Clean(document.QuerySelector(".product-about__description, [itemprop='description'], [data-qaid='product_description'], [data-qaid='descriptions']")?.InnerHtml ?? "", 10000);
            if (pageDescription.Length > description.Length) description = pageDescription;
            var brand = product.ValueKind == JsonValueKind.Object && product.TryGetProperty("brand", out var brandNode)
                ? brandNode.ValueKind == JsonValueKind.String ? brandNode.GetString() : Text(brandNode, "name")
                : Text(epicMetadata, "vendorName") ?? document.QuerySelector("[itemprop='brand'] [itemprop='name']")?.GetAttribute("content") ?? "";
            var images = new List<string>();
            if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("image", out var imageNode))
                foreach (var image in imageNode.ValueKind == JsonValueKind.Array ? imageNode.EnumerateArray().ToArray() : [imageNode])
                    AddImage(images, image.ValueKind == JsonValueKind.String ? image.GetString() : Text(image, "url") ?? Text(image, "contentUrl"), source);
            if (images.Count == 0) AddImage(images, Meta(document, "og:image"), source);
            foreach (var image in document.QuerySelectorAll("img[data-product-image], [data-qaid='image_link'] img, img[itemprop='image']"))
                AddImage(images, image.GetAttribute("data-zoom") ?? image.GetAttribute("src"), source);
            var characteristics = new List<ImportedCharacteristicDto>();
            if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("additionalProperty", out var properties) && properties.ValueKind == JsonValueKind.Array)
                foreach (var property in properties.EnumerateArray().Take(100))
                {
                    var key = Clean(Text(property, "name") ?? "", 200); var value = Clean(Text(property, "value") ?? "", 2000);
                    if (key.Length > 0 && value.Length > 0) characteristics.Add(new(key, value));
                }
            foreach (var row in document.QuerySelectorAll(provider == "epic" ? "dl > div" : "[data-qaid='attributes'] tr, [data-qaid='attribute_block'] tr"))
            {
                var cells = row.QuerySelectorAll(provider == "epic" ? "dt,dd" : "th,td");
                if (cells.Length != 2) continue;
                var key = Clean(cells[0].TextContent, 200).TrimEnd(':'); var value = Clean(cells[1].TextContent, 2000);
                if (key.Length > 0 && value.Length > 0 && characteristics.Count < 100 && !characteristics.Any(c => c.Name == key && c.Value == value)) characteristics.Add(new(key, value));
            }
            AddIdentifier(characteristics, "Артикул производителя", Text(product, "mpn"));
            AddIdentifier(characteristics, "Штрихкод", Text(product, "gtin13") ?? Text(product, "gtin14") ?? Text(product, "gtin12") ?? Text(product, "gtin8") ?? Text(product, "gtin"));
            decimal? price = null;
            if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("offers", out var offers))
                foreach (var offer in offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().ToArray() : [offers])
                    if (Text(offer, "priceCurrency") is null or "UAH" && decimal.TryParse(Text(offer, "price") ?? Text(offer, "lowPrice"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and <= 999999999.99m)
                    { price = decimal.Round(parsed, 2); break; }
            if (price is null && (Meta(document, "product:price:currency") is null or "UAH")
                && decimal.TryParse(Meta(document, "product:price:amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var metaPrice) && metaPrice is > 0 and <= 999999999.99m) price = decimal.Round(metaPrice, 2);
            if (price is null && decimal.TryParse(Text(epicMetadata, "productPrice"), NumberStyles.Number, CultureInfo.InvariantCulture, out var epicPrice) && epicPrice is > 0 and <= 999999999.99m) price = decimal.Round(epicPrice, 2);
            return new(id, source.AbsoluteUri, name, Clean(description, 10000), Clean(brand ?? "", 200), categoryKey, categoryName, price, images.Take(8).ToList(), characteristics);
        }
        finally { foreach (var json in documents) json.Dispose(); }
    }
    private static bool IsProduct(Uri uri) { try { MarketplaceSourceClient.ProductUri(uri.AbsoluteUri); return true; } catch (ArgumentException) { return false; } }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
    private static void AddImage(List<string> images, string? value, Uri? source = null)
    {
        if (string.IsNullOrWhiteSpace(value) || images.Count >= 8) return;
        try
        {
            if (source is not null && Uri.TryCreate(source, value, out var resolved)) value = resolved.AbsoluteUri;
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = "https://" + value[7..];
            var uri = MarketplaceSourceClient.ImageUri(value); if (!images.Contains(uri.AbsoluteUri, StringComparer.Ordinal)) images.Add(uri.AbsoluteUri);
        }
        catch (ArgumentException) { }
    }
    private static MarketplaceProductData ParseRozetkaApi(Uri source, string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
        var root = document.RootElement;
        var expectedId = Regex.Match(source.AbsolutePath, @"/p([0-9]+)/?$").Groups[1].Value;
        if (!root.TryGetProperty("data", out var data) || Text(data, "id") != expectedId)
            throw new BusinessRuleException("Rozetka вернула другой товар. Повторите импорт ссылки.");
        var name = Clean(Text(data, "title") ?? "", 250);
        var categoryId = Text(data, "category_id") ?? "";
        if (name.Length == 0 || !Regex.IsMatch(categoryId, @"^[1-9][0-9]{0,11}$")) throw new BusinessRuleException("Rozetka вернула неполные данные товара.");
        var categoryName = data.TryGetProperty("last_category", out var category) ? Text(category, "title") ?? "" : "";
        var images = new List<string>();
        if (data.TryGetProperty("images", out var imageList) && imageList.ValueKind == JsonValueKind.Array)
            foreach (var image in imageList.EnumerateArray())
                foreach (var size in new[] { "big", "original", "large" })
                    if (image.TryGetProperty(size, out var sized)) { AddImage(images, Text(sized, "url")); break; }
        var description = data.TryGetProperty("description", out var descriptionNode) ? Text(descriptionNode, "text") : null;
        if (string.IsNullOrWhiteSpace(description)) description = Text(data, "docket");
        var characteristics = new List<ImportedCharacteristicDto>();
        if (root.TryGetProperty("trebaCharacteristics", out var groups) && groups.ValueKind == JsonValueKind.Array)
            foreach (var group in groups.EnumerateArray())
                if (group.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                    foreach (var option in options.EnumerateArray())
                    {
                        var title = Clean(Text(option, "title") ?? "", 200);
                        var values = option.TryGetProperty("values", out var list) && list.ValueKind == JsonValueKind.Array
                            ? string.Join(", ", list.EnumerateArray().Select(v => Text(v, "title") ?? "")) : Text(option, "value") ?? "";
                        var value = Clean(values, 2000);
                        if (title.Length > 0 && value.Length > 0 && characteristics.Count < 100) characteristics.Add(new(title, value));
                    }
        AddIdentifier(characteristics, "Артикул производителя", Text(data, "article"));
        AddIdentifier(characteristics, "Штрихкод", Text(data, "barcode") ?? Text(data, "gtin"));
        decimal? price = decimal.TryParse(Text(data, "price"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount is > 0 and <= 999999999.99m ? decimal.Round(amount, 2) : null;
        return new(expectedId, source.AbsoluteUri, name, Clean(description ?? "", 10000), Clean(Text(data, "brand") ?? "", 200), "c" + categoryId,
            Clean(categoryName, 250), price, images, characteristics);
    }
    private static void AddIdentifier(List<ImportedCharacteristicDto> values, string name, string? raw)
    {
        var value = Clean(raw ?? "", 100);
        if (value.Length > 0 && !values.Any(v => v.Name == name && v.Value == value)) values.Insert(0, new(name, value));
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
