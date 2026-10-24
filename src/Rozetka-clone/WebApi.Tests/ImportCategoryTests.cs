using Application.Catalog;
using Application.Common;
using Application.Products;
using Domain.Entities;
using Domain.Entities.Telegram;
using Microsoft.EntityFrameworkCore;
using WebApi.Administration;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class ImportCategoryTests
{
    private static MarketplaceProductData Data(string category, string name = "Product") =>
        new("123", "https://rozetka.com.ua/test/p123/", name, "", "", "c123", category, null, [], []);

    private static Category Category(string name, bool active = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = active,
            Slug = Guid.NewGuid().ToString("N"),
        };

    [Theory]
    [InlineData("Frying pans", "Сковороди")]
    [InlineData("Laptops", "Ноутбуки")]
    [InlineData("Мобільні телефони", "Смартфони")]
    public void EquivalentSourceNames_ResolveToExistingCategory(string source, string target)
    {
        var category = Category(target);
        Assert.Equal(category.Id, ImportCategoryResolver.Resolve(Data(source, "Apple iPhone 17 Pro"), [category]));
    }

    [Fact]
    public void InactiveAmbiguousOrUnrelatedCategories_AreNotGuessed()
    {
        Assert.Null(ImportCategoryResolver.Resolve(Data("Сковороди"), [Category("Сковороди", false)]));
        Assert.Null(ImportCategoryResolver.Resolve(Data("Сковороди"), [Category("Сковороди"), Category("Сковороди")]));
        Assert.Null(ImportCategoryResolver.Resolve(Data("Посуд"), [Category("Сковороди")]));
        Assert.Null(ImportCategoryResolver.Resolve(Data("Мобільні телефони", "Nokia 105"), [Category("Смартфони")]));
        var buttonPhone = Data("Мобільні телефони", "Test Smartphone") with
        {
            Characteristics = [new("Клас", "Кнопкові телефони")],
        };
        Assert.Null(ImportCategoryResolver.Resolve(buttonPhone, [Category("Смартфони")]));
    }

    [Fact]
    public async Task AliasPreview_StillRequiresSellerPermissions_AndMappingWins()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        seed.Category.Name = "Смартфони";
        await db.SaveChangesAsync();
        var source = new PhoneSource();
        var importer = new ProductImportService(
            db,
            source,
            new(),
            new(db),
            new(db),
            new ProductService(db),
            new TelegramTestData.Media(),
            new TelegramTestData.Clock()
        );
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default)
        );
        db.SellerCategoryGrants.Add(
            new()
            {
                SellerId = seed.Seller.Id,
                CategoryId = seed.Root.Id,
                IncludeDescendants = true,
            }
        );
        await db.SaveChangesAsync();
        var preview = await importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        Assert.Equal(seed.Category.Id, preview.CategoryId);
        db.RozetkaCategoryMappings.Add(new() { ExternalKey = "c80001", CategoryId = seed.Root.Id });
        await db.SaveChangesAsync();
        Assert.Equal(
            seed.Root.Id,
            (await importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default)).CategoryId
        );
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            importer.SubmitAsync(seed.User.Id, preview.Id, TelegramTestData.Submit(seed), default)
        );
    }

    [Fact]
    public async Task Deployment_AddsMissingCookwareOnce_PreservesMappingsAndInheritedPermissions()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        seed.Category.Name = "Для приготування їжі";
        seed.Category.Slug = ImportCatalogDeployment.CookwareSlug;
        db.SellerCategoryGrants.Add(
            new()
            {
                SellerId = seed.Seller.Id,
                CategoryId = seed.Root.Id,
                IncludeDescendants = true,
            }
        );
        await db.SaveChangesAsync();
        Assert.True(await new SellerCategoryAccess(db).CanClassifyUnmappedImportsAsync(seed.User.Id, default));
        await ImportCatalogDeployment.ApplyAsync(db, default);
        var pan = await db.Categories.SingleAsync(c => c.Name == "Сковороди");
        Assert.Equal(seed.Category.Id, pan.ParentId);
        Assert.Equal(seed.Category.Level + 1, pan.Level);
        Assert.Equal(pan.Id, (await db.RozetkaCategoryMappings.SingleAsync()).CategoryId);
        Assert.True(await new SellerCategoryAccess(db).CanClassifyUnmappedImportsAsync(seed.User.Id, default));
        // An administrator's mapping/disabled category is not overwritten by subsequent deployments.
        pan.IsActive = false;
        (await db.RozetkaCategoryMappings.SingleAsync()).CategoryId = seed.Root.Id;
        await db.SaveChangesAsync();
        await ImportCatalogDeployment.ApplyAsync(db, default);
        Assert.Equal(10, await db.Categories.CountAsync());
        Assert.False(pan.IsActive);
        Assert.Equal(seed.Root.Id, (await db.RozetkaCategoryMappings.SingleAsync()).CategoryId);
    }

    private sealed class PhoneSource : IMarketplaceSourceClient
    {
        public Task<string> GetPageAsync(Uri uri, CancellationToken ct) =>
            Task.FromResult(
                TelegramTestData
                    .Html.Replace("Phones", "Мобільні телефони")
                    .Replace("Test <b>Phone</b>", "Apple iPhone 17 Pro")
                    .Replace("<script>bad()</script>", "<script>bad()<\\/script>", StringComparison.Ordinal)
            );

        public Task<byte[]> GetImageAsync(Uri uri, CancellationToken ct) => throw new InvalidOperationException();
    }
}
