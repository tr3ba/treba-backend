using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Application.Common;
using Contracts.Telegram;

namespace WebApi.Telegram;

// Reads published product data without running third-party JavaScript or importing page markup.
public sealed class GenericProductParser
{
    public MarketplaceProductData Parse(Uri source, string html)
    {
        using var document = new HtmlParser().ParseDocument(html);
        if (document.QuerySelector("#challenge-running, #challenge-form, script[src*='challenge-platform'], iframe[src*='captcha']") is not null
            || document.Title?.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) == true)
            throw new BusinessRuleException("Сайт обмежив автоматичне завантаження. Спробуйте пізніше або додайте товар вручну.");
        var documents = new List<JsonDocument>();
        try
        {
            foreach (var script in document.QuerySelectorAll("script[type='application/ld+json'], script[type='application/json']").Take(32))
                try { documents.Add(JsonDocument.Parse(script.TextContent, new JsonDocumentOptions { MaxDepth = 48 })); }
                catch (JsonException) { /* Other independent metadata blocks may still be valid. */ }
            var nodes = documents.SelectMany(json => Walk(json.RootElement)).Take(10000).ToList();
            var references = nodes.Where(node => Text(node, "@id") is not null)
                .GroupBy(node => Text(node, "@id")!).ToDictionary(group => group.Key, group => group.OrderByDescending(node => node.EnumerateObject().Count()).First());
            JsonElement Resolve(JsonElement node) => Text(node, "@id") is { } id && references.TryGetValue(id, out var actual) ? actual : node;
            var candidates = nodes.Where(node => Type(node, "Product") || Type(node, "ProductGroup")).ToList();
            var product = candidates.OrderByDescending(node => SamePage(source, Text(node, "url") ?? Text(node, "@id")) ? 10 : Type(node, "ProductGroup") ? 2 : 0).FirstOrDefault();
            if (candidates.Count > 1 && nodes.Any(node => Type(node, "ItemList"))
                && !SamePage(source, Text(product, "url") ?? Text(product, "@id")))
                throw new BusinessRuleException("Відкрийте сторінку конкретного товару, а не список товарів.");
            // Shopify and other stores often publish product JSON without a schema.org @type.
            if (product.ValueKind == JsonValueKind.Undefined)
                product = nodes.FirstOrDefault(node => Text(node, "title") is not null && Property(node, "variants").ValueKind == JsonValueKind.Array
                    && (Property(node, "images").ValueKind == JsonValueKind.Array || Text(node, "handle") is not null));
            var scope = document.QuerySelector("[itemscope][itemtype*='schema.org/Product']:not([itemtype*='ProductGroup']), main .product, .product-single, .product-detail") ?? document.Body ?? document.DocumentElement;
            string Micro(string name) => ElementValue(scope.QuerySelector($"[itemprop~='{name}']"));
            var microProduct = document.QuerySelector("[itemtype*='schema.org/Product']") is not null;
            var metaPrice = Meta(document, "product:price:amount") ?? Meta(document, "og:price:amount") ?? Micro("price");
            var domPrice = ElementValue(scope.QuerySelector(".price ins .amount, [data-product-price], .current-price") ?? scope.QuerySelector(".price .amount, .product-price"));
            if (product.ValueKind != JsonValueKind.Object && !microProduct && Meta(document, "og:type") is not ("product" or "product.item")
                && (string.IsNullOrWhiteSpace(metaPrice) && string.IsNullOrWhiteSpace(domPrice)))
                throw new BusinessRuleException("На сторінці не знайдено відкритих даних товару. Якщо сайт завантажує їх лише через JavaScript, додайте товар вручну.");
            var name = Clean(Text(product, "name") ?? Text(product, "title") ?? Empty(Micro("name")) ?? document.QuerySelector("h1")?.TextContent ?? Meta(document, "og:title"), 250);
            if (name.Length == 0) throw new BusinessRuleException("Не вдалося прочитати назву товару.");
            var description = Clean(Text(product, "description") ?? Text(product, "body_html") ?? Empty(Micro("description")) ?? Meta(document, "og:description") ?? Meta(document, "description"), 10000);
            var fullDescription = Clean(scope.QuerySelector("#tab-description, .woocommerce-product-details__short-description, .product-description, .product__description, [data-product-description]")?.InnerHtml, 10000);
            if (fullDescription.Length > description.Length) description = fullDescription;
            var brandNode = Resolve(Property(product, "brand"));
            var brand = Clean(Scalar(brandNode) ?? Text(product, "vendor") ?? Empty(ElementValue(scope.QuerySelector("[itemprop='brand'] [itemprop='name']"))) ?? Empty(Micro("brand")) ?? Meta(document, "product:brand"), 200);
            var characteristics = new List<ImportedCharacteristicDto>();
            void Add(string key, string? raw)
            {
                key = Clean(key, 200).TrimEnd(':');
                var value = Clean(raw, 2000);
                if (key.Length > 0 && value.Length > 0 && characteristics.Count < 100 && !characteristics.Any(item => item.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && item.Value == value))
                    characteristics.Add(new(key, value));
            }
            foreach (var property in Items(Property(product, "additionalProperty")))
                Add(Text(property, "name") ?? "", Scalar(Property(property, "value")) + (Text(property, "unitText") is { } unit ? " " + unit : ""));
            foreach (var row in scope.QuerySelectorAll("table tr"))
            {
                var cells = row.QuerySelectorAll("th,td");
                if (cells.Length == 2) Add(cells[0].TextContent, cells[1].TextContent);
            }
            foreach (var dt in scope.QuerySelectorAll("dl dt"))
                if (dt.NextElementSibling is { LocalName: "dd" } dd) Add(dt.TextContent, dd.TextContent);
            foreach (var property in scope.QuerySelectorAll("[itemprop~='additionalProperty']"))
                Add(ElementValue(property.QuerySelector("[itemprop~='name']")), ElementValue(property.QuerySelector("[itemprop~='value']")));
            Add("SKU", Text(product, "sku") ?? Empty(Micro("sku")));
            Add("Артикул виробника", Text(product, "mpn") ?? Empty(Micro("mpn")));
            Add("Штрихкод", Text(product, "gtin13") ?? Text(product, "gtin14") ?? Text(product, "gtin12") ?? Text(product, "gtin8") ?? Text(product, "gtin") ?? Empty(Micro("gtin13")) ?? Empty(Micro("gtin")));
            foreach (var key in new[] { "color", "size", "material", "weight", "width", "height", "depth", "model" })
                Add(key, Scalar(Property(product, key)));

            var images = new List<string>();
            void Image(string? value)
            {
                if (string.IsNullOrWhiteSpace(value) || images.Count >= 8 || !Uri.TryCreate(source, value, out var uri)) return;
                try
                {
                    if (uri.Scheme == "http") uri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
                    var image = MarketplaceSourceClient.ImageUri(uri.AbsoluteUri, allowExternal: true).AbsoluteUri;
                    if (!images.Contains(image, StringComparer.Ordinal)) images.Add(image);
                }
                catch (ArgumentException) { /* Invalid images never enter the draft or downloader. */ }
            }
            foreach (var image in Items(Property(product, "image")).Concat(Items(Property(product, "images"))))
                Image(image.ValueKind == JsonValueKind.String ? image.GetString() : Text(image, "url") ?? Text(image, "contentUrl") ?? Text(image, "src"));
            foreach (var image in document.QuerySelectorAll("meta[property='og:image'], meta[property='og:image:secure_url']")) Image(image.GetAttribute("content"));
            foreach (var image in scope.QuerySelectorAll("[itemprop~='image'], .woocommerce-product-gallery img, .product-gallery img, .product__media img, img[data-product-image], [data-product-images] img"))
                Image(image.GetAttribute("data-large_image") ?? image.GetAttribute("data-zoom-image") ?? image.GetAttribute("data-src") ?? image.GetAttribute("src") ?? image.GetAttribute("content") ?? image.GetAttribute("href"));

            var offers = Items(Property(product, "offers")).Select(Resolve).ToList();
            var offer = offers.FirstOrDefault();
            if (Property(offer, "offers").ValueKind is JsonValueKind.Array or JsonValueKind.Object) offer = Items(Property(offer, "offers")).Select(Resolve).FirstOrDefault();
            var currency = Clean(Text(offer, "priceCurrency") ?? Empty(Micro("priceCurrency")) ?? Meta(document, "product:price:currency") ?? Meta(document, "og:price:currency"), 10).ToUpperInvariant();
            var rawPrice = Text(offer, "price") ?? Text(offer, "lowPrice") ?? Text(Resolve(Property(offer, "priceSpecification")), "price") ?? Empty(metaPrice) ?? Empty(domPrice);
            if (currency.Length == 0 && Regex.IsMatch(rawPrice ?? "", @"₴|грн|UAH", RegexOptions.IgnoreCase)) currency = "UAH";
            var price = ParsePrice(rawPrice);
            Add("Наявність", Text(offer, "availability")?.Split('/').Last() ?? Empty(Micro("availability"))?.Split('/').Last());
            Add("Валюта джерела", Empty(currency));
            if (price.HasValue) Add("Ціна джерела", price.Value.ToString(CultureInfo.InvariantCulture) + " " + currency);
            else if (!string.IsNullOrWhiteSpace(rawPrice)) Add("Ціна джерела", rawPrice + " " + currency);

            var variants = Items(Property(product, "hasVariant")).Concat(Items(Property(product, "variants"))).Select(Resolve).Take(40).ToList();
            foreach (var variant in variants)
            {
                var variantOffers = Items(Property(variant, "offers")).Select(Resolve).FirstOrDefault();
                var variantName = Text(variant, "name") ?? Text(variant, "title") ?? Text(variant, "sku") ?? "";
                var details = new[] { Text(variant, "sku"), Text(variant, "color"), Text(variant, "size"), Text(variant, "barcode"), Text(variantOffers, "price"), Text(variantOffers, "priceCurrency"), Text(variantOffers, "availability")?.Split('/').Last() };
                Add("Варіант: " + variantName, string.Join(" · ", details.Where(value => !string.IsNullOrWhiteSpace(value))));
            }
            // Group metadata has no price of its own; one unambiguous variant may supply it.
            if (price is null && variants.Count == 1)
            {
                var variant = variants[0]; var variantOffer = Items(Property(variant, "offers")).Select(Resolve).FirstOrDefault();
                var variantCurrency = Text(variantOffer, "priceCurrency");
                price = ParsePrice(Text(variantOffer, "price"));
                if (variantCurrency is not null) { currency = variantCurrency.ToUpperInvariant(); Add("Валюта джерела", currency); }
                Add("SKU", Text(variant, "sku")); Add("Штрихкод", Text(variant, "barcode") ?? Text(variant, "gtin13"));
            }
            // Never interpret a foreign price as UAH. Its original value remains in source characteristics.
            if (currency != "UAH") price = null;
            var categories = new List<string>(); var categoryIdentity = "";
            foreach (var crumb in nodes.Where(node => Type(node, "BreadcrumbList")))
                foreach (var item in Items(Property(crumb, "itemListElement")))
                {
                    var link = Resolve(Property(item, "item"));
                    var url = link.ValueKind == JsonValueKind.String ? link.GetString() : Text(link, "@id") ?? Text(link, "url");
                    var title = Clean(Text(item, "name") ?? Text(link, "name"), 250);
                    if (Uri.TryCreate(source, url, out var uri) && uri.Host == source.Host && !SamePage(source, uri.AbsoluteUri) && uri.AbsolutePath.Trim('/').Length > 0 && title.Length > 0)
                    { categories.Add(title); categoryIdentity = uri.AbsolutePath; }
                }
            if (categories.Count == 0)
                foreach (var link in document.QuerySelectorAll("[itemtype*='BreadcrumbList'] a[href], .breadcrumbs a[href], .breadcrumb a[href]"))
                {
                    if (!Uri.TryCreate(source, link.GetAttribute("href"), out var uri) || uri.Host != source.Host || SamePage(source, uri.AbsoluteUri) || uri.AbsolutePath.Trim('/').Length == 0) continue;
                    var title = Clean(link.TextContent, 250);
                    if (title.Length > 0) { categories.Add(title); categoryIdentity = uri.AbsolutePath; }
                }
            var category = categories.LastOrDefault() ?? Clean(Text(product, "category") ?? Text(product, "type") ?? "Категорію джерела не вказано", 250);
            return new("web:" + Hash(source.AbsoluteUri, 24), source.AbsoluteUri, name, description, brand,
                "web:" + Hash(source.IdnHost + ":" + (categoryIdentity.Length > 0 ? categoryIdentity : category), 12), category, price, images, characteristics) { CategoryPath = categories };
        }
        finally { foreach (var json in documents) json.Dispose(); }
    }

    private static string Hash(string value, int length) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..length].ToLowerInvariant();
    private static string Clean(string? text, int max) => MarketplaceProductParser.Clean(text ?? "", max);
    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string ElementValue(IElement? element) => element?.GetAttribute("content") ?? element?.GetAttribute("value") ?? element?.GetAttribute("href") ?? element?.GetAttribute("src") ?? element?.TextContent ?? "";
    private static string? Meta(IDocument doc, string key) => doc.QuerySelector($"meta[property='{key}'], meta[name='{key}']")?.GetAttribute("content");
    private static JsonElement Property(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;
    private static string? Text(JsonElement node, string key) => Scalar(Property(node, key));
    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        JsonValueKind.Object => Scalar(Property(value, "name")) ?? Scalar(Property(value, "value")),
        JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Select(Scalar).Where(item => item is not null)),
        _ => null,
    };
    private static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : value.ValueKind is JsonValueKind.Object or JsonValueKind.String ? [value] : [];
    private static IEnumerable<JsonElement> Walk(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) { yield return value; foreach (var child in value.EnumerateObject()) foreach (var node in Walk(child.Value)) yield return node; }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) foreach (var node in Walk(child)) yield return node;
    }
    private static bool Type(JsonElement node, string type) => Items(Property(node, "@type")).Any(value => value.ValueKind == JsonValueKind.String && value.GetString()?.TrimEnd('/').Split('/').Last() == type);
    private static bool SamePage(Uri source, string? value) => !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(source, value, out var uri) && uri.Host == source.Host && uri.AbsolutePath.TrimEnd('/') == source.AbsolutePath.TrimEnd('/');
    private static decimal? ParsePrice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = Regex.Replace(value, @"[\s\u00a0\u202f]", "");
        if (Regex.IsMatch(value, @"(^|[^\d])-\d")) return null;
        var number = Regex.Match(value, @"^[^\d]*([0-9]+(?:[.,][0-9]+)*)").Groups[1].Value;
        if (Regex.IsMatch(number, @"^[0-9]+[.,][0-9]{3}$")) return null;
        if (number.Contains(',') && number.Contains('.')) number = number.LastIndexOf(',') > number.LastIndexOf('.') ? number.Replace(".", "").Replace(',', '.') : number.Replace(",", "");
        else if (number.Contains(',')) number = number.Replace(',', '.');
        return decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) && price is > 0 and <= 999999999.99m ? decimal.Round(price, 2) : null;
    }
}
