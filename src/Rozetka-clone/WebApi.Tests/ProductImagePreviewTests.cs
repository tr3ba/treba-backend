using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AdminPanel.Services.Implementations;
using Microsoft.Extensions.Configuration;

namespace WebApi.Tests;

public sealed class ProductImagePreviewTests
{
    [Fact]
    public void BrowserPhoto_UsesPublicOrigin_InsteadOfInternalDockerAddress()
    {
        using var http = new HttpClient { BaseAddress = new("http://host.docker.internal:5000/") };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["PublicApiBaseUrl"] = "https://treba.duckdns.org/" }
            )
            .Build();
        var client = new CatalogApiClient(http, configuration);
        Assert.Equal(
            "https://treba.duckdns.org/uploads/products/photo.webp",
            client.ResolveImageUrl("/uploads/products/photo.webp")
        );
        Assert.Equal(
            "https://content.rozetka.com.ua/photo.webp",
            client.ResolveImageUrl("https://content.rozetka.com.ua/photo.webp")
        );
        http.BaseAddress = new("http://localhost:5511/");
        Assert.Equal(
            "http://localhost:5511/uploads/products/photo.webp",
            new CatalogApiClient(http).ResolveImageUrl("/uploads/products/photo.webp")
        );
    }

    [Fact]
    public async Task AvailablePhoto_ReturnsDataUrl()
    {
        using var http = Client(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("image/png") },
                    },
                }
            )
        );

        Assert.Equal("data:image/png;base64,AQID", await new CatalogApiClient(http).ImageDataUrl("/uploads/photo.png"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UnavailablePhoto_AllowsEditorToUsePlaceholder(HttpStatusCode status)
    {
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(status)));

        Assert.Null(await new CatalogApiClient(http).ImageDataUrl("/uploads/missing.png"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthenticationFailure_IsNotTreatedAsMissingPhoto(HttpStatusCode status)
    {
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(status)));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new CatalogApiClient(http).ImageDataUrl("/uploads/photo.png")
        );
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task DisconnectedPhotoServer_AllowsEditorToUsePlaceholder()
    {
        using var http = Client(_ =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused."))
        );

        Assert.Null(await new CatalogApiClient(http).ImageDataUrl("https://media.example.test/photo.png"));
    }

    [Fact]
    public async Task PhotoRequestTimeout_AllowsEditorToUsePlaceholder()
    {
        using var http = Client(_ => Task.FromException<HttpResponseMessage>(new TaskCanceledException()));

        Assert.Null(await new CatalogApiClient(http).ImageDataUrl("/uploads/photo.png"));
    }

    [Fact]
    public async Task HtmlResponse_IsNotRenderedAsPhoto()
    {
        using var http = Client(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html>Photo not found</html>", Encoding.UTF8, "text/html"),
                }
            )
        );

        Assert.Null(await new CatalogApiClient(http).ImageDataUrl("/uploads/photo.png"));
    }

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new ImageHandler(send)) { BaseAddress = new Uri("http://localhost/") };

    private sealed class ImageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request);
    }
}
