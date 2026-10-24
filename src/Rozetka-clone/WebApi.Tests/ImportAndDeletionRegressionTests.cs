using System.Net;
using System.Net.Http.Json;
using Application.Catalog;
using Application.Common;
using Application.Products;
using Contracts.Catalog;
using Contracts.Common;
using Contracts.Telegram;
using Domain.Entities;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class ImportAndDeletionRegressionTests
{
    [Fact]
    public void ResistanceBands_UseClosestExistingSportsSection_AndRejectAmbiguousParents()
    {
        var root = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Спорт та захоплення",
            IsActive = true,
        };
        var fitness = new Category
        {
            Id = Guid.NewGuid(),
            ParentId = root.Id,
            Name = "Фітнес",
            IsActive = true,
        };
        var data = new MarketplaceProductData(
            "123",
            TelegramTestData.SourceUrl,
            "Гумова петля BraveU",
            "",
            "",
            "c4669267",
            "Еспандери",
            null,
            [],
            []
        );
        Assert.Equal(fitness.Id, ImportCategoryResolver.Resolve(data, [root, fitness]));
        Assert.Equal(root.Id, ImportCategoryResolver.Resolve(data, [root]));
        var leaf = new Category
        {
            Id = Guid.NewGuid(),
            ParentId = fitness.Id,
            Name = "Еспандери",
            IsActive = true,
        };
        Assert.Equal(leaf.Id, ImportCategoryResolver.Resolve(data, [root, fitness, leaf]));
        Assert.Null(
            ImportCategoryResolver.Resolve(
                data,
                [
                    root,
                    fitness,
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Name = "Фітнес",
                        IsActive = true,
                    },
                ]
            )
        );
        Assert.Equal(
            fitness.Id,
            ImportCategoryResolver.Resolve(
                data with
                {
                    CategoryName = "Новий спортивний аксесуар",
                    CategoryPath = [root.Name, fitness.Name],
                },
                [root, fitness]
            )
        );
    }

    [Fact]
    public async Task MissingCategory_CanBeCreatedUnderFitness_ReusedOnNextImport_AndCannotCrossSellerScope()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        seed.Root.Name = "Спорт та захоплення";
        seed.Category.Name = "Фітнес";
        db.SellerCategoryGrants.Add(
            new()
            {
                SellerId = seed.Seller.Id,
                CategoryId = seed.Root.Id,
                IncludeDescendants = true,
            }
        );
        await db.SaveChangesAsync();
        var source = new FixedSource(TelegramTestData.Html.Replace("Phones", "Еспандери"));
        var imports = new ProductImportService(
            db,
            source,
            new(),
            new(db),
            new(db),
            new ProductService(db),
            new TelegramTestData.Media(),
            new TelegramTestData.Clock()
        );
        var preview = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        Assert.Equal(seed.Category.Id, preview.CategoryId);
        Assert.True(preview.CanClassifyCategory);
        var child = await imports.CreateCategoryAsync(
            seed.User.Id,
            preview.Id,
            new("  Еспандери  ", seed.Category.Id),
            default
        );
        Assert.Equal(
            child.CategoryId,
            (
                await imports.CreateCategoryAsync(seed.User.Id, preview.Id, new("ЕСПАНДЕРИ", seed.Category.Id), default)
            ).CategoryId
        );
        Assert.Equal(
            child.CategoryId,
            (await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default)).CategoryId
        );
        Assert.Equal(seed.Category.Id, (await db.Categories.SingleAsync(c => c.Id == child.CategoryId)).ParentId);
        Assert.Equal(3, await db.Categories.CountAsync());
        Assert.Empty(await db.Products.ToListAsync());
        var options = await imports.OptionsAsync(seed.User.Id, default);
        Assert.Contains(
            options.Categories,
            c => c.Id == child.CategoryId && c.Name == "Спорт та захоплення → Фітнес → Еспандери"
        );
        var other = await TelegramTestData.SeedAsync(db);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            imports.CreateCategoryAsync(other.User.Id, preview.Id, new("Інша", seed.Root.Id), default)
        );
    }

    [Fact]
    public async Task CategoryCreation_RechecksMappingPermissionsExpiryAndForeignDraft()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        db.SellerCategoryGrants.Add(
            new()
            {
                SellerId = seed.Seller.Id,
                CategoryId = seed.Root.Id,
                IncludeDescendants = true,
            }
        );
        await db.SaveChangesAsync();
        var clock = new TelegramTestData.Clock();
        var imports = TelegramTestData.Import(db, clock, new(), new());
        var preview = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        var other = await TelegramTestData.UserAsync(db); // Active user without an active seller.
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            imports.CreateCategoryAsync(other.Id, preview.Id, new("Denied", seed.Root.Id), default)
        );
        db.RozetkaCategoryMappings.Add(new() { ExternalKey = "c80001", CategoryId = seed.Category.Id });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            imports.CreateCategoryAsync(seed.User.Id, preview.Id, new("Denied", seed.Root.Id), default)
        );
        db.RozetkaCategoryMappings.RemoveRange(await db.RozetkaCategoryMappings.ToListAsync());
        await db.SaveChangesAsync();
        clock.Now = clock.Now.AddHours(2);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            imports.CreateCategoryAsync(seed.User.Id, preview.Id, new("Expired", seed.Root.Id), default)
        );
        Assert.Equal(2, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task BrandMissingAtSource_ReusesPreviouslyCreatedBrandFromProductTitle()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        await TelegramTestData.AllowImportAsync(db, seed);
        var html = TelegramTestData
            .Html.Replace(",\"brand\":{\"name\":\"Test Brand\"}", "")
            .Replace("Test <b>Phone</b>", "Гумова петля BraveU");
        var imports = new ProductImportService(
            db,
            new FixedSource(html),
            new(),
            new(db),
            new(db),
            new ProductService(db),
            new TelegramTestData.Media(),
            new TelegramTestData.Clock()
        );
        var first = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        Assert.Null(first.BrandId);
        var brand = await imports.CreateBrandAsync(seed.User.Id, first.Id, "BraveU", default);
        var second = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        Assert.Equal(brand.Id, second.BrandId);
        Assert.Equal("BraveU", second.BrandName);
        Assert.Equal(2, await db.Brands.CountAsync());
    }

    [Theory]
    [InlineData("\"brand\":{\"name\":\"BraveU\"},")]
    [InlineData("")]
    public void RozetkaApi_ResolvesBrandObjectOrManufacturerPartNumber(string brand)
    {
        var json =
            "{\"data\":{\"id\":123456,\"category_id\":4669267,\"title\":\"Гумова петля BraveU\",\"article\":\"BraveU-47mm\","
            + brand
            + "\"last_category\":{\"title\":\"Еспандери\"}}}";
        var data = new MarketplaceProductParser().Parse(new(TelegramTestData.SourceUrl), json);
        Assert.Equal("BraveU", data.BrandName);
        Assert.Contains(data.Characteristics, c => c.Name == "Артикул виробника" && c.Value == "BraveU-47mm");
    }

    [Fact]
    public async Task Delete_HidesOwnProductAndDuplicates_PreservesRows_AndAllowsImportAgain()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        await TelegramTestData.AllowImportAsync(db, seed);
        var imports = TelegramTestData.Import(db, new(), new(), new());
        var preview = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        var request = TelegramTestData.Submit(seed);
        request.ManufacturerPartNumber = "BraveU-5";
        var id = await imports.SubmitAsync(seed.User.Id, preview.Id, request, default);
        Assert.Equal(id, (await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default)).ProductId);
        var catalog = new CatalogService(db);
        Assert.False(await catalog.DeleteAsync(id, Guid.NewGuid(), default));
        Assert.True(await catalog.DeleteAsync(id, seed.User.Id, default));
        Assert.True(await catalog.DeleteAsync(id, seed.User.Id, default));
        Assert.Equal(ProductStatus.ARCHIVED, (await db.Products.SingleAsync()).Status);
        Assert.False((await db.ProductVariants.SingleAsync()).IsActive);
        Assert.Single(await db.ProductImages.ToListAsync());
        Assert.Empty((await catalog.ListAsync(1, 20, null, null, false, null, null, default, seed.User.Id)).Content);
        Assert.Equal(0, (await catalog.StatsAsync(default, seed.User.Id)).Total);
        Assert.Empty(
            await new CatalogDuplicateService(db).FindAsync(
                seed.User.Id,
                seed.Brand.Id,
                "BraveU-5",
                null,
                null,
                default
            )
        );
        var again = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        Assert.Null(again.ProductId);
        Assert.Null(again.SubmittedAt);
        Assert.NotEqual(preview.Id, again.Id);
    }

    [Fact]
    public async Task TwoPreviewsOfTheSameSource_SubmitToOneProduct()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        await TelegramTestData.AllowImportAsync(db, seed);
        var imports = TelegramTestData.Import(db, new(), new(), new());
        var first = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        var second = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        var id = await imports.SubmitAsync(seed.User.Id, first.Id, TelegramTestData.Submit(seed), default);
        Assert.Equal(id, await imports.SubmitAsync(seed.User.Id, second.Id, TelegramTestData.Submit(seed), default));
        Assert.Single(await db.Products.ToListAsync());
        Assert.All(await db.ProductImportDrafts.ToListAsync(), d => Assert.Equal(id, d.ProductId));
    }

    [Fact]
    public async Task DeleteHttp_EnforcesSellerOwnershipAndStaffRoles()
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        Guid owner,
            id;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seed = await CatalogTestData.SeedAsync(db);
            owner = seed.Seller.UserId;
            id = (
                await new CatalogService(db).SaveAsync(null, seed.Store.Id, CatalogTestData.Request(seed), default)
            )!.Id;
        }
        client.DefaultRequestHeaders.Add("X-Test-Role", "Moderator");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"/api/v1/admin/catalog/products/{id}")).StatusCode
        );
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Seller");
        client.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/v1/admin/catalog/products/{id}")).StatusCode
        );
        client.DefaultRequestHeaders.Remove("X-Test-UserId");
        client.DefaultRequestHeaders.Add("X-Test-UserId", owner.ToString());
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/v1/admin/catalog/products/{id}")).StatusCode
        );
        Assert.Empty(
            (await client.GetFromJsonAsync<PagedResponse<CatalogProductDto>>("/api/v1/admin/catalog/products"))!.Content
        );
    }

    [Theory]
    [InlineData("ru", "uk")]
    [InlineData("ru-RU", "uk")]
    [InlineData("uk-UA", "uk")]
    [InlineData("en-US", "en")]
    public void Profile_OnlyStoresSupportedLanguages(string language, string expected) =>
        Assert.Equal(expected, UserProfile.Create(Guid.NewGuid(), language: language).Language);

    [Fact]
    public async Task Retry_CanExplicitlyResetPreviousCategoryToAutomatic()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        await TelegramTestData.AllowImportAsync(db, seed);
        var queue = new SellerImportQueue(db, new(db), new TelegramTestData.Clock());
        var job = Assert.Single(
            await queue.AddAsync(seed.User.Id, new([new(TelegramTestData.SourceUrl, seed.Category.Id)]), default)
        );
        var entity = await db.SellerImportJobs.SingleAsync(j => j.Id == job.Id);
        entity.Status = "Failed";
        await db.SaveChangesAsync();
        await queue.RetryAsync(seed.User.Id, job.Id, default, resetCategory: true);
        Assert.Null((await db.SellerImportJobs.SingleAsync()).CategoryId);
    }

    private sealed class FixedSource(string html) : IMarketplaceSourceClient
    {
        public Task<string> GetPageAsync(Uri uri, CancellationToken ct) =>
            Task.FromResult(
                html.Replace("<script>bad()</script>", "<script>bad()<\\/script>", StringComparison.Ordinal)
            );

        public Task<byte[]> GetImageAsync(Uri uri, CancellationToken ct) => throw new InvalidOperationException();
    }
}
