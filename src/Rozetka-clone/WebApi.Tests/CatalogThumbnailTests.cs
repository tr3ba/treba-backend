using System.Text.Json;
using AdminPanel.Components;
using AdminPanel.Services.Abstractions;
using AdminPanel.Services.Implementations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebApi.Tests;

public sealed class CatalogThumbnailTests
{
    [Fact]
    public void CategoryTree_DeserializesThePhotoReturnedByTheApi()
    {
        var category = JsonSerializer.Deserialize<CategoryTreeDto>(
            """
            {"id":"11111111-1111-1111-1111-111111111111","name":"Laptops","slug":"laptops","isActive":true,"children":[],"imageUrl":"https://content.rozetka.com.ua/laptops.png"}
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        Assert.Equal("https://content.rozetka.com.ua/laptops.png", category!.ImageUrl);
    }

    [Theory]
    [InlineData("/uploads/products/photo.webp", "https://treba.duckdns.org/uploads/products/photo.webp")]
    [InlineData("https://content.rozetka.com.ua/laptops.png", "https://content.rozetka.com.ua/laptops.png")]
    [InlineData(null, null)]
    public async Task Thumbnail_RendersLazyPhoto_AndNeverUsesAFolderOrBox(string? input, string? expected)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?> { ["PublicApiBaseUrl"] = "https://treba.duckdns.org/" }
                    )
                    .Build()
            )
            .AddSingleton(new HttpClient { BaseAddress = new("http://host.docker.internal:5000/") })
            .AddTransient<CatalogApiClient>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (
                await renderer.RenderComponentAsync<CatalogThumbnail>(
                    ParameterView.FromDictionary(
                        new Dictionary<string, object?> { [nameof(CatalogThumbnail.ImageUrl)] = input }
                    )
                )
            ).ToHtmlString()
        );
        if (expected is null)
            Assert.Contains("bi-image", html);
        else
        {
            Assert.Contains(expected, html);
            Assert.Contains("loading=\"lazy\"", html);
        }
        Assert.DoesNotContain("bi-folder", html);
        Assert.DoesNotContain("bi-box", html);
        Assert.DoesNotContain("host.docker.internal", html);
    }
}
