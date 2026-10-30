using System.Net;
using System.Text;
using System.ComponentModel.DataAnnotations;
using Application.Catalog;
using Application.Products;
using Application.Common;
using Contracts.Telegram;
using Microsoft.EntityFrameworkCore;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class GenericImportTests
{
    private static MarketplaceProductData Parse(string html, string url = "https://shop.example.test/products/studio") => new MarketplaceProductParser().Parse(new(url), html);

    [Fact]
    public void PublicStoreUrls_KeepVariantAndProductQueries_WithoutTrackingOrMarketplacePrivilege()
    {
        var uri = MarketplaceSourceClient.ProductUri("https://shop.example.test/item?id=42&variant=blue&utm_source=test&fbclid=x#reviews");
        Assert.Equal("?id=42&variant=blue", uri.Query);
        Assert.Empty(uri.Fragment);
        Assert.Equal("web", MarketplaceSourceClient.Provider(uri));
        Assert.Equal("web", MarketplaceSourceClient.Provider(new("https://prom.ua.evil.test/item")));
        Assert.Equal("https://images.example.test/photo.jpg", MarketplaceSourceClient.ImageUri("https://images.example.test/photo.jpg", true).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://localhost/item")]
    [InlineData("https://metadata.google.internal/item")]
    [InlineData("https://shop.local/item")]
    [InlineData("https://127.0.0.1/item")]
    [InlineData("https://169.254.169.254/item")]
    [InlineData("https://[::1]/item")]
    [InlineData("https://user:password@shop.example.test/item")]
    [InlineData("https://shop.example.test:444/item")]
    [InlineData("http://shop.example.test/item")]
    public void GenericUrls_RejectInternalNetworksCredentialsAndUnsafeProtocols(string url)
    {
        Assert.Throws<ArgumentException>(() => MarketplaceSourceClient.ProductUri(url));
        Assert.Throws<ArgumentException>(() => MarketplaceSourceClient.ImageUri(url, true));
    }

    [Fact]
    public void JsonLdGraph_ResolvesOffersAndBrand_PreservesIdentifiersImagesSpecsAndCategory()
    {
        var data = Parse("""
            <script type="application/ld+json">{"@graph":[
              {"@id":"#brand","@type":"Brand","name":"Acme"},
              {"@id":"#offer","@type":"Offer","price":"1 234,50","priceCurrency":"UAH","availability":"https://schema.org/InStock"},
              {"@type":"Product","url":"https://shop.example.test/products/studio","name":"Studio <img src=x onerror=bad()>","description":"<p>Full description</p><p>Second paragraph</p>","brand":{"@id":"#brand"},"sku":"ST-001","mpn":"AC-42","gtin13":"4820000000001","offers":{"@id":"#offer"},
               "image":["/images/a.jpg",{"contentUrl":"https://cdn.example.test/b.webp"},"data:image/svg+xml,test","https://localhost/secret"],
               "additionalProperty":[{"name":"Memory","value":"128","unitText":"GB"},{"name":"Colors","value":["Blue","White"]}]},
              {"@type":"BreadcrumbList","itemListElement":[{"name":"Home","item":"/"},{"name":"Audio","item":{"@id":"/audio"}},{"name":"Studio","item":"/products/studio"}]}]}</script>
            """);
        Assert.Equal("Studio", data.Name);
        Assert.Equal("Acme", data.BrandName);
        Assert.Equal(1234.50m, data.Price);
        Assert.Contains("Second paragraph", data.Description);
        Assert.DoesNotContain("bad()", data.Name);
        Assert.Equal("Audio", data.CategoryName);
        Assert.Equal(["Audio"], data.CategoryPath);
        Assert.StartsWith("web:", data.Id);
        Assert.True(data.Id.Length <= 30 && data.CategoryKey.Length + 2 <= 20);
        Assert.Equal(2, data.Images.Count);
        Assert.Equal("https://shop.example.test/images/a.jpg", data.Images[0]);
        Assert.Contains(data.Characteristics, value => value.Name == "SKU" && value.Value == "ST-001");
        Assert.Contains(data.Characteristics, value => value.Name == "Штрихкод" && value.Value == "4820000000001");
        Assert.Contains(data.Characteristics, value => value.Name == "Memory" && value.Value == "128 GB");
    }

    [Fact]
    public void MicrodataAndHtml_ReadProductFieldsGalleryAndSpecificationRows()
    {
        var data = Parse("""
            <main itemscope itemtype="https://schema.org/Product"><h1 itemprop="name">Studio</h1>
            <meta itemprop="brand" content="Acme"><meta itemprop="sku" content="STUDIO">
            <div itemprop="offers" itemscope itemtype="https://schema.org/Offer"><meta itemprop="priceCurrency" content="UAH"><meta itemprop="price" content="99.95"></div>
            <p itemprop="description">Useful product</p><div class="product-gallery"><img data-src="/img/main.png"></div>
            <table><tr><th>Color:</th><td>Blue</td></tr></table><dl><dt>Weight</dt><dd>2 kg</dd></dl></main>
            """);
        Assert.Equal(99.95m, data.Price);
        Assert.Equal("Acme", data.BrandName);
        Assert.Single(data.Images);
        Assert.Contains(data.Characteristics, value => value.Name == "Color" && value.Value == "Blue");
        Assert.Contains(data.Characteristics, value => value.Name == "Weight" && value.Value == "2 kg");
    }

    [Fact]
    public void OpenGraphFallback_DoesNotConvertForeignPricesIntoHryvnias()
    {
        var data = Parse("""
            <meta property="og:type" content="product"><meta property="og:title" content="Product">
            <meta property="product:price:amount" content="1,299.95"><meta property="product:price:currency" content="USD">
            <meta property="og:image" content="/photo.jpg"><meta name="description" content="Product description">
            """);
        Assert.Null(data.Price);
        Assert.Contains(data.Characteristics, value => value.Name == "Валюта джерела" && value.Value == "USD");
        Assert.Contains(data.Characteristics, value => value.Name == "Ціна джерела" && value.Value == "1299.95 USD");
    }

    [Theory]
    [InlineData("-10")]
    [InlineData("1,299")]
    public void NegativeOrAmbiguousPrices_RequireManualReview_AndRetainTheOriginalValue(string price)
    {
        var data = Parse("""
            <script type="application/ld+json">{"@type":"Product","name":"Studio","offers":{"price":"SOURCE_PRICE","priceCurrency":"UAH"}}</script>
            """.Replace("SOURCE_PRICE", price));
        Assert.Null(data.Price);
        Assert.Contains(data.Characteristics, value => value.Name == "Ціна джерела" && value.Value == price + " UAH");
    }

    [Fact]
    public void ProductGroups_RetainVariantInformation_AndNeverGuessOnePriceForMultipleVariants()
    {
        var data = Parse("""
            <script type="application/ld+json">{"@type":"ProductGroup","name":"Studio","hasVariant":[
              {"@type":"Product","name":"Blue","sku":"BLUE","color":"Blue","offers":{"price":"999","priceCurrency":"UAH"}},
              {"@type":"Product","name":"White","sku":"WHITE","color":"White","offers":{"price":"1499","priceCurrency":"UAH"}}]}</script>
            """);
        Assert.Equal("Studio", data.Name);
        Assert.Null(data.Price);
        Assert.Equal(2, data.Characteristics.Count(value => value.Name.StartsWith("Варіант:")));
        Assert.Contains(data.Characteristics, value => value.Value.Contains("WHITE") && value.Value.Contains("1499"));
    }

    [Theory]
    [InlineData("<h1>Category</h1><a href='/item'>Product</a>")]
    [InlineData("<title>Just a moment...</title><form id='challenge-form'></form>")]
    [InlineData("<div id='app'></div><script src='/application.js'></script>")]
    public void MissingOrBlockedProductData_ProducesAnActionableError(string html) => Assert.Throws<BusinessRuleException>(() => Parse(html));

    [Fact]
    public async Task PublicDownloads_ValidateEveryRedirect_AndNeverContactThePrivateDestination()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            return new(HttpStatusCode.Redirect) { Headers = { Location = new("https://169.254.169.254/latest/meta-data/") } };
        }));
        using var client = new MarketplaceSourceClient(http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetPageAsync(new("https://shop.example.test/item"), default));
        Assert.Equal(1, calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task GenericPreview_CanBeReviewedAndSubmitted_WithExternalImagesAndOwnInventory()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        await TelegramTestData.AllowImportAsync(db, seed);
        var media = new TelegramTestData.Media();
        var importer = new ProductImportService(db, new GenericSource(), new(), new(db), new CatalogService(db), new ProductService(db), media, new TelegramTestData.Clock());
        var preview = await importer.PreviewAsync(seed.User.Id, "https://shop.example.test/products/studio", default, seed.Category.Id);
        Assert.Equal("SOURCE-SKU", preview.SourceSku);
        Assert.Equal("UAH", preview.SourceCurrency);
        Assert.Equal("4820000000001", preview.Barcode);
        Assert.Single(preview.SourceWarnings);
        Assert.Equal(seed.Brand.Id, preview.BrandId);
        Assert.True(preview.CanClassifyCategory);
        Assert.Empty(await db.Products.ToListAsync());
        var request = TelegramTestData.Submit(seed);
        request.Price = preview.SourcePrice!.Value;
        request.StockQuantity = 7;
        var id = await importer.SubmitAsync(seed.User.Id, preview.Id, request, default);
        Assert.Equal(1, await db.ProductImages.CountAsync(image => image.ProductId == id));
        Assert.Single(media.Files);
        Assert.Equal(7, (await db.ProductVariants.SingleAsync(variant => variant.ProductId == id)).StockQuantity);
        Assert.Equal(id, await importer.SubmitAsync(seed.User.Id, preview.Id, request, default));
        var input = new CategoryMappingInput("web:abcdef123456", seed.Category.Id);
        Assert.True(Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }

    private sealed class GenericSource : IMarketplaceSourceClient
    {
        public Task<string> GetPageAsync(Uri uri, CancellationToken ct) => Task.FromResult("""
            <script type="application/ld+json">{"@type":"Product","name":"Generic studio","brand":"Test Brand","sku":"SOURCE-SKU",
              "image":"https://cdn.example.test/studio.png","offers":{"price":"199.95","priceCurrency":"UAH"},"additionalProperty":[{"name":"EAN","value":["4820000000001","4820000000002"]}]}</script>
            """);
        public Task<byte[]> GetImageAsync(Uri uri, CancellationToken ct) => Task.FromResult(new byte[] { 137,80,78,71,13,10,26,10,0 });
    }
}
