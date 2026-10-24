using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using WebApi.Media;

namespace WebApi.Tests;

public sealed class ProductMediaStorageTests
{
    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "MediaTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task SharedRoot_RetainsPhotoAcrossApplicationInstances_AndDeletesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "treba-media-test-" + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        var file = Guid.NewGuid().ToString("N") + ".png";
        var options = Options.Create(new ProductMediaOptions { RootPath = root });
        using var first = new ProductMediaStorage(options, new EnvironmentStub(root));
        using var second = new ProductMediaStorage(options, new EnvironmentStub(root));
        try
        {
            await first.SaveAsync(id, file, [1, 2, 3], default);
            var loaded = await second.ReadAsync(id, file, default);
            Assert.NotNull(loaded);
            Assert.Equal("image/png", loaded.ContentType);
            Assert.Equal(new byte[] { 1, 2, 3 }, loaded.Bytes);
            await second.DeleteAsync(id, file, default);
            Assert.Null(await second.ReadAsync(id, file, default));
        }
        finally
        {
            if (
                Path.GetFullPath(root)
                    .StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(root)
            )
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("../../credentials.json")]
    [InlineData("photo.png\nput /secret /public")]
    public async Task MediaPaths_RejectTraversalAndCommandInjection(string name)
    {
        using var storage = new ProductMediaStorage(
            Options.Create(new ProductMediaOptions()),
            new EnvironmentStub(Path.GetTempPath())
        );
        await Assert.ThrowsAsync<ArgumentException>(() => storage.ReadAsync(Guid.NewGuid(), name, default));
    }
}
