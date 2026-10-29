using Application.Common;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Tests;

public sealed class SellerImportTests
{
    [Fact]
    public async Task Brand_IsMatchedAutomatically_AndExplicitCreationReusesNormalizedNames()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        await TelegramTestData.AllowImportAsync(db, seed);
        var clock = new TelegramTestData.Clock();
        var imports = TelegramTestData.Import(db, clock, new(), new());
        var preview = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        Assert.Equal(seed.Brand.Id, preview.BrandId);
        Assert.Single(await db.Brands.ToListAsync());
        Assert.Empty(await db.Products.ToListAsync());
        Assert.Equal(
            seed.Brand.Id,
            (await imports.CreateBrandAsync(seed.User.Id, preview.Id, "  TEST   brand  ", default)).Id
        );
        var created = await imports.CreateBrandAsync(seed.User.Id, preview.Id, " Acme   Labs ", default);
        Assert.Equal("Acme Labs", created.Name);
        Assert.Equal(created.Id, (await imports.CreateBrandAsync(seed.User.Id, preview.Id, "ACME LABS", default)).Id);
        Assert.Equal(2, await db.Brands.CountAsync());
        var request = TelegramTestData.Submit(seed);
        request.BrandId = created.Id;
        var productId = await imports.SubmitAsync(seed.User.Id, preview.Id, request, default);
        Assert.Equal(created.Id, (await db.Products.SingleAsync(p => p.Id == productId)).BrandId);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            imports.CreateBrandAsync(seed.User.Id, preview.Id, "Another brand", default)
        );
    }

    [Fact]
    public async Task BrandCreation_RequiresOwnUnexpiredPreview_CurrentCategoryAccess_AndActiveAccount()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        await TelegramTestData.AllowImportAsync(db, seed);
        var other = await TelegramTestData.SeedAsync(db);
        var clock = new TelegramTestData.Clock();
        var imports = TelegramTestData.Import(db, clock, new(), new());
        var preview = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            imports.CreateBrandAsync(other.User.Id, preview.Id, "Foreign", default)
        );
        seed.Brand.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            imports.CreateBrandAsync(seed.User.Id, preview.Id, "Test Brand", default)
        );
        var grants = await db.SellerCategoryGrants.ToListAsync();
        db.RemoveRange(grants);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            imports.CreateBrandAsync(seed.User.Id, preview.Id, "Forbidden", default)
        );
        db.AddRange(grants);
        await db.SaveChangesAsync();
        clock.Now = clock.Now.AddHours(2);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            imports.CreateBrandAsync(seed.User.Id, preview.Id, "Expired", default)
        );
        Assert.DoesNotContain(await db.Brands.ToListAsync(), b => b.Name is "Foreign" or "Forbidden" or "Expired");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BrandCreation_RejectsBlockedSeller_OrUnavailableStore(bool blocked)
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        await TelegramTestData.AllowImportAsync(db, seed);
        var imports = TelegramTestData.Import(db, new(), new(), new());
        var preview = await imports.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        if (blocked)
            seed.User.Block();
        else
            seed.Store.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            imports.CreateBrandAsync(seed.User.Id, preview.Id, "Denied brand", default)
        );
        Assert.Single(await db.Brands.ToListAsync());
    }

}
