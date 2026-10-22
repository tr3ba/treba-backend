using Application.Catalog;
using Domain.Entities;
using Domain.Entities.Telegram;
using Microsoft.EntityFrameworkCore;
using WebApi.Administration;

namespace WebApi.Tests;

public sealed class FullCatalogSnapshotTests
{
    [Fact]
    public void EmbeddedSnapshot_CoversTheEntireReviewedSource_WithUniqueKeysAndImages()
    {
        var snapshot = RozetkaCatalogDeployment.LoadSnapshot();
        RozetkaCatalogDeployment.Validate(snapshot);
        Assert.Equal(4632, snapshot.Categories.Count);
        Assert.All(snapshot.Categories, c => Assert.False(string.IsNullOrWhiteSpace(c.ImageUrl)));
        Assert.Equal(18, snapshot.Categories.Count(c => c.ParentKey is null));
        Assert.Contains(snapshot.Categories, c => c.Key == "c80003");
        Assert.Contains(snapshot.Categories, c => c.Key == "c4626754");
    }

    [Fact]
    public async Task Apply_IsIdempotent_PreservesExistingIdsEditsAndRestrictedSellers_AndExtendsFullCatalogScope()
    {
        await using var db = TelegramTestData.Memory(); var seed = await TelegramTestData.SeedAsync(db);
        db.SellerCategoryGrants.Add(new() { SellerId = seed.Seller.Id, CategoryId = seed.Root.Id, IncludeDescendants = true });
        var restricted = await TelegramTestData.SeedAsync(db);
        restricted.Root.Slug = "restricted-root"; restricted.Category.Slug = "restricted-phones";
        db.SellerCategoryGrants.Add(new() { SellerId = restricted.Seller.Id, CategoryId = restricted.Category.Id });
        // Full seller must cover both existing branches before expansion.
        db.SellerCategoryGrants.Add(new() { SellerId = seed.Seller.Id, CategoryId = restricted.Root.Id, IncludeDescendants = true }); await db.SaveChangesAsync();
        var snapshot = Sample(seed.Root.Slug, seed.Category.Slug);
        var first = await RozetkaCatalogDeployment.ApplyAsync(db, snapshot, default);
        Assert.Equal(2, first.AddedCategories); Assert.Equal(4, first.AddedMappings); Assert.Equal(1, first.FullCatalogSellers);
        Assert.Equal(seed.Category.Id, (await db.RozetkaCategoryMappings.SingleAsync(m => m.ExternalKey == "c2")).CategoryId);
        Assert.Equal(seed.Root.Id, seed.Category.ParentId);
        Assert.Equal("Source phones", seed.Category.Name); Assert.NotNull(seed.Category.ImageUrl);
        var access = new SellerCategoryAccess(db);
        Assert.True(await access.CanClassifyUnmappedImportsAsync(seed.User.Id, default));
        Assert.False(await access.CanClassifyUnmappedImportsAsync(restricted.User.Id, default));
        Assert.Single(await access.AllowedIdsAsync(restricted.User.Id, default));
        seed.Category.Name = "Operator edited"; seed.Category.ImageUrl = "https://images.example.test/custom.webp"; seed.Category.IsActive = false; await db.SaveChangesAsync();
        var grantCount = await db.SellerCategoryGrants.CountAsync();
        var second = await RozetkaCatalogDeployment.ApplyAsync(db, snapshot, default);
        Assert.Equal(0, second.AddedCategories); Assert.Equal(0, second.AddedMappings); Assert.Equal(0, second.UpdatedImages);
        Assert.Equal(grantCount, await db.SellerCategoryGrants.CountAsync());
        Assert.Equal("Operator edited", seed.Category.Name); Assert.EndsWith("custom.webp", seed.Category.ImageUrl); Assert.False(seed.Category.IsActive);
        Assert.Empty(await db.Products.ToListAsync());
    }

    [Fact]
    public async Task NewChildren_RespectADisabledBranch_WithoutRemovingFullAccessToActiveCategories()
    {
        await using var db = TelegramTestData.Memory(); var seed = await TelegramTestData.SeedAsync(db);
        seed.Category.IsActive = false;
        db.SellerCategoryGrants.Add(new() { SellerId = seed.Seller.Id, CategoryId = seed.Root.Id, IncludeDescendants = true }); await db.SaveChangesAsync();
        await RozetkaCatalogDeployment.ApplyAsync(db, Sample(seed.Root.Slug, seed.Category.Slug), default);
        var childId = (await db.RozetkaCategoryMappings.SingleAsync(m => m.ExternalKey == "c3")).CategoryId;
        Assert.False((await db.Categories.SingleAsync(c => c.Id == childId)).IsActive);
        Assert.False(seed.Category.IsActive);
        Assert.True(await new SellerCategoryAccess(db).CanClassifyUnmappedImportsAsync(seed.User.Id, default));
    }

    [Fact]
    public async Task ExistingAdministratorMapping_RemainsAuthoritative()
    {
        await using var db = TelegramTestData.Memory(); var seed = await TelegramTestData.SeedAsync(db);
        db.RozetkaCategoryMappings.Add(new() { ExternalKey = "c2", CategoryId = seed.Root.Id }); await db.SaveChangesAsync();
        await RozetkaCatalogDeployment.ApplyAsync(db, Sample(seed.Root.Slug, seed.Category.Slug), default);
        Assert.Equal(seed.Root.Id, (await db.RozetkaCategoryMappings.SingleAsync(m => m.ExternalKey == "c2")).CategoryId);
        Assert.Equal("Phones", seed.Category.Name);
    }

    [Fact]
    public async Task InvalidSourceGraph_FailsBeforeWritingAnyRows()
    {
        await using var db = TelegramTestData.Memory();
        var snapshot = Sample("root", "phones");
        var broken = snapshot with { Categories = snapshot.Categories.Reverse().ToList() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => RozetkaCatalogDeployment.ApplyAsync(db, broken, default));
        Assert.Empty(await db.Categories.ToListAsync()); Assert.Empty(await db.RozetkaCategoryMappings.ToListAsync());
    }

    internal static RozetkaCategorySnapshot Sample(string root, string phones) => new("test", "https://rozetka.com.ua/ua/all-categories-goods/", [
        new("c1", null, "Source electronics", "https://rozetka.com.ua/ua/c1/", 0, root, "Electronics", "https://content.rozetka.com.ua/root.png", "category"),
        new("c2", "c1", "Source phones", "https://rozetka.com.ua/ua/c2/", 0, phones, "Phones", "https://content.rozetka.com.ua/phone.png", "category"),
        new("c3", "c2", "New subtype", "https://rozetka.com.ua/ua/c3/", 0, null, null, "https://content.rozetka.com.ua/subtype.png", "category"),
        new("c4", null, "New root", "https://rozetka.com.ua/ua/c4/", 1, null, null, "https://content.rozetka.com.ua/new-root.png", "category")]);
}
