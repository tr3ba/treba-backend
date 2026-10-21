using System.Net;
using System.Text;
using Application.Common;
using Domain.Entities;
using Domain.Entities.Telegram;
using Microsoft.EntityFrameworkCore;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class MarketplaceImportTests
{
    [Theory]
    [InlineData("https://rozetka.com.ua/ua/example/p622282721/?utm_source=test", "rozetka")]
    [InlineData("https://prom.ua/ua/p123456-test-product.html", "prom")]
    [InlineData("https://seller.prom.ua/p123456-test-product.html", "prom")]
    [InlineData("https://epicentrk.ua/ua/shop/test-product.html#description", "epic")]
    public void ProductUrls_AcceptOnlySupportedProductRoutes_AndRemoveTracking(string url, string provider)
    {
        var parsed = MarketplaceSourceClient.ProductUri(url);
        Assert.Equal(provider, MarketplaceSourceClient.Provider(parsed));
        Assert.Empty(parsed.Query); Assert.Empty(parsed.Fragment);
    }

    [Theory]
    [InlineData("https://prom.ua.evil.test/ua/p123-test.html")]
    [InlineData("https://epicentrk.ua/ua/shop/category/")]
    [InlineData("https://prom.ua/ua/catalog.html")]
    [InlineData("https://prom.ua:444/ua/p123-test.html")]
    [InlineData("https://product-api.rozetka.com.ua/v4/goods/get-main?goodsId=123")]
    [InlineData("http://prom.ua/ua/p123-test.html")]
    public void ProductUrls_RejectUntrustedHostsAndNonProductPages(string url) => Assert.Throws<ArgumentException>(() => MarketplaceSourceClient.ProductUri(url));

    [Theory]
    [InlineData("https://prom.ua/ua/p123456-test-product.html", "https://images.prom.ua/123.jpg", "prom:")]
    [InlineData("https://epicentrk.ua/ua/shop/test-product.html", "https://cdn.epicentrk.ua/upload/123.jpg", "epic:")]
    public void StructuredData_ImportsOtherMarketplacesWithSeparateIds(string url, string image, string prefix)
    {
        var html = """
            <html><head><script type="application/ld+json">
            {"@type":"Product","name":"Example product","description":"<p>Useful product</p>","brand":{"name":"Example"},
             "image":["IMAGE","https://evil.test/file.jpg"],"category":"Phones","offers":{"priceCurrency":"UAH","price":"123.45"},
             "additionalProperty":[{"name":"Memory","value":"128 GB"}]}</script></head></html>
            """.Replace("IMAGE", image);
        var product = new MarketplaceProductParser().Parse(new Uri(url), html);
        Assert.StartsWith(prefix, product.Id); Assert.True(product.Id.Length <= 30);
        Assert.Equal("Example product", product.Name); Assert.Equal(123.45m, product.Price);
        Assert.Equal(image, Assert.Single(product.Images)); Assert.Single(product.Characteristics);
        Assert.Equal("Useful product", product.Description); Assert.True(product.CategoryKey.Length <= 18);
    }

    private const string MainJson = """
        {"success":true,"data":{"id":622282721,"title":"ASUS Example","category_id":80004,"brand":"ASUS","price":12345,
          "last_category":{"title":"Ноутбуки"},"docket":"<p>8 GB / SSD 512 GB</p>","description":{"text":null},
          "images":[{"big":{"url":"https://content.rozetka.com.ua/goods/images/big/123.jpg"}}]},
          "trebaCharacteristics":[{"options":[{"title":"RAM","values":[{"title":"8 GB"}]}]}]}
        """;
    private static readonly Uri RozetkaUrl = new("https://rozetka.com.ua/ua/example/p622282721/");

    [Fact]
    public void Epicentr_ReadsServerRenderedProductWithoutExecutingPageScripts()
    {
        var product = new MarketplaceProductParser().Parse(new("https://epicentrk.ua/ua/shop/example.html"), """
            <html><head><meta property="og:title" content="Long SEO heading">
            <script>window.dataLayer.push({"pageType":"ProductPage","productName":"Example laptop","productPrice":12345,"categoryId":179,"categoryName":"Ноутбуки","vendorName":"Example"});</script></head>
            <body><h1 data-product-name>Example laptop</h1><img data-product-image data-zoom="https://cdn.27.ua/original/example.jpg">
            <dl><div><dt>RAM:</dt><dd>8 GB</dd></div></dl></body></html>
            """);
        Assert.Equal("Example laptop", product.Name); Assert.Equal("Ноутбуки", product.CategoryName);
        Assert.Equal("Example", product.BrandName); Assert.Equal(12345m, product.Price);
        Assert.Equal("https://cdn.27.ua/original/example.jpg", Assert.Single(product.Images)); Assert.Single(product.Characteristics);
    }

    [Fact]
    public void Prom_ReadsFullDescriptionAndTableCharacteristics()
    {
        var product = new MarketplaceProductParser().Parse(new("https://prom.ua/ua/p123-example.html"), """
            <html><head><script type="application/ld+json">{"@type":"Product","name":"Example","description":"Short description"}</script></head>
            <body><div data-qaid="descriptions"><p>Full product description including contents and specifications.</p></div>
            <section data-qaid="attributes"><table><tr><th>RAM</th><td>8 GB</td></tr></table></section></body></html>
            """);
        Assert.StartsWith("Full product description", product.Description); Assert.Equal("RAM", Assert.Single(product.Characteristics).Name);
    }

    [Fact]
    public void RozetkaApi_ReadsProductCategoryPhotosAndCharacteristics_AndChecksIdentity()
    {
        var parser = new MarketplaceProductParser(); var product = parser.Parse(RozetkaUrl, MainJson);
        Assert.Equal("ASUS Example", product.Name); Assert.Equal("c80004", product.CategoryKey);
        Assert.Equal("Ноутбуки", product.CategoryName); Assert.Equal("8 GB / SSD 512 GB", product.Description);
        Assert.Single(product.Images); Assert.Equal("8 GB", Assert.Single(product.Characteristics).Value);
        Assert.Throws<BusinessRuleException>(() => parser.Parse(RozetkaUrl, MainJson.Replace("622282721", "622282722")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Source_UsesPublicRozetkaData_WhenHtmlIsBlocked_AndToleratesUnavailableExtraDetails(bool unavailableDetails)
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request => {
            requests.Add(request.RequestUri!);
            Assert.Equal("product-api.rozetka.com.ua", request.RequestUri!.Host);
            return request.RequestUri.AbsolutePath.EndsWith("get-main") ? Json(MainJson)
                : unavailableDetails ? new(HttpStatusCode.ServiceUnavailable) : Json("""{"success":true,"data":[]} """);
        }));
        using var source = new MarketplaceSourceClient(http);
        var data = new MarketplaceProductParser().Parse(RozetkaUrl, await source.GetPageAsync(RozetkaUrl, default));
        Assert.Equal("ASUS Example", data.Name); Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task Source_FallsBackToPublicHtml_IfApiIsUnavailable()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request => {
            calls++;
            return request.RequestUri!.Host == "product-api.rozetka.com.ua" ? new(HttpStatusCode.Forbidden)
                : new(HttpStatusCode.OK) { Content = new StringContent("<html><h1>Product</h1></html>", Encoding.UTF8, "text/html") };
        }));
        using var source = new MarketplaceSourceClient(http);
        Assert.Contains("<h1>Product</h1>", await source.GetPageAsync(RozetkaUrl, default)); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Source_DoesNotFollowRedirectsToPrivateOrUnrelatedOrigins()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://169.254.169.254/latest/meta-data/"); return response; }));
        using var source = new MarketplaceSourceClient(http);
        await Assert.ThrowsAsync<ArgumentException>(() => source.GetPageAsync(RozetkaUrl, default)); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnmappedCategory_FullCatalogSellerCanClassify_AndPermissionsAreRecheckedBeforeSubmission()
    {
        await using var db = TelegramTestData.Memory(); var seed = await TelegramTestData.SeedAsync(db);
        db.Add(new SellerCategoryGrant { SellerId = seed.Seller.Id, CategoryId = seed.Root.Id, IncludeDescendants = true }); await db.SaveChangesAsync();
        var importer = TelegramTestData.Import(db, new(), new(), new());
        var preview = await importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        Assert.Equal(seed.Category.Id, preview.CategoryId); Assert.Empty(await db.Products.ToListAsync());
        // A new root without permission removes full-catalog access, even though this category remains allowed.
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Books", Slug = "books", IsActive = true }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => importer.SubmitAsync(seed.User.Id, preview.Id, TelegramTestData.Submit(seed), default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default, seed.Category.Id));
        Assert.Empty(await db.Products.ToListAsync());
    }

    [Fact]
    public async Task UnmappedCategory_ExplicitChoiceMustBeAllowed_AndNewMappingCannotBeOverridden()
    {
        await using var db = TelegramTestData.Memory(); var seed = await TelegramTestData.SeedAsync(db);
        db.Add(new SellerCategoryGrant { SellerId = seed.Seller.Id, CategoryId = seed.Root.Id, IncludeDescendants = true }); await db.SaveChangesAsync();
        var importer = TelegramTestData.Import(db, new(), new(), new());
        await Assert.ThrowsAsync<BusinessRuleException>(() => importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default, Guid.NewGuid()));
        var preview = await importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default, seed.Category.Id);
        db.Add(new RozetkaCategoryMapping { ExternalKey = "c80001", CategoryId = seed.Root.Id }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => importer.SubmitAsync(seed.User.Id, preview.Id, TelegramTestData.Submit(seed), default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default, seed.Category.Id));
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
