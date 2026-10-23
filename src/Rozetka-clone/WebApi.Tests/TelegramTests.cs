using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Application.Catalog;
using Application.Common;
using Domain.Entities.Users;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class TelegramTests
{
    [Theory]
    [InlineData("treba_marketplace_bot")]
    [InlineData("@treba_marketplace_bot")]
    [InlineData("  @treba_marketplace_bot  ")]
    public void Configuration_AcceptsTheBotFatherUsernameWithAnOptionalAtSign(string username)
    {
        var options = new TelegramOptions { Enabled = true, BotToken = TelegramTestData.BotToken,
            BotUsername = username, MiniAppUrl = "https://treba.duckdns.org/telegram/" };
        Assert.True(options.Configured);
        Assert.Equal("treba_marketplace_bot", options.BotUsername);
    }

    [Fact]
    public void InitData_ValidatesSignatureAgeDuplicatesAndTelegramUser()
    {
        var clock = new TelegramTestData.Clock(); var validator = new TelegramInitDataValidator(clock); var maxAge = TimeSpan.FromMinutes(30);
        var signed = TelegramTestData.Signed(12345, clock.Now);
        Assert.Equal(12345, validator.Validate(signed, TelegramTestData.BotToken, maxAge).Id);
        foreach (var invalid in new[] { signed.Replace("query_id=test-query", "query_id=tampered"), signed + "&auth_date=1",
            TelegramTestData.Signed(12345, clock.Now.AddMinutes(-31)), TelegramTestData.Signed(12345, clock.Now.AddMinutes(1)),
            TelegramTestData.Signed(12345, clock.Now, "wrong-token"), TelegramTestData.Signed(-1, clock.Now), TelegramTestData.Signed(12345, clock.Now, isBot: true) })
            Assert.Throws<UnauthorizedAccessException>(() => validator.Validate(invalid, TelegramTestData.BotToken, maxAge));
        Assert.Throws<UnauthorizedAccessException>(() => validator.Validate(signed[..^64] + new string('z',64), TelegramTestData.BotToken, maxAge));
    }

    [Theory]
    [InlineData(Roles.Customer)]
    public async Task Link_RejectsOtherRoles(string role)
    {
        await using var db = TelegramTestData.Memory(); var user = await TelegramTestData.UserAsync(db, role);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new TelegramLinkService(db, TelegramTestData.Options, new TelegramTestData.Clock()).BeginAsync(user.Id, default));
        Assert.Empty(await db.TelegramLinkChallenges.ToListAsync());
    }

    [Fact]
    public async Task Link_RequiresWebsiteConsent_IsSingleUse_AndCanBeRevoked()
    {
        await using var db = TelegramTestData.Memory(); var user = await TelegramTestData.UserAsync(db); var clock = new TelegramTestData.Clock();
        var links = new TelegramLinkService(db, TelegramTestData.Options, clock); var start = await links.BeginAsync(user.Id, default); var secret = start.BotUrl.Split("link_")[1];
        Assert.DoesNotContain(secret, (await db.TelegramLinkChallenges.SingleAsync()).TokenHash);
        await Assert.ThrowsAsync<BusinessRuleException>(() => links.ConfirmAsync(user.Id, start.Id, default));
        await links.ClaimAsync(secret, 12345, 12345, "test_user", "Telegram User", default);
        Assert.Empty(await db.TelegramBindings.ToListAsync()); Assert.Equal(start.Id, (await links.StatusAsync(user.Id, default)).PendingId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => links.ClaimAsync(secret, 56789, 56789, null, "Other", default));
        Assert.True((await links.ConfirmAsync(user.Id, start.Id, default)).Linked);
        await Assert.ThrowsAsync<BusinessRuleException>(() => links.ConfirmAsync(user.Id, start.Id, default));
        await links.UnlinkAsync(user.Id, default); Assert.False((await links.StatusAsync(user.Id, default)).Linked);
        await Assert.ThrowsAsync<BusinessRuleException>(() => links.ClaimAsync(secret, 12345, 12345, null, "Old", default));
        var expired = await links.BeginAsync(user.Id, default); clock.Now = clock.Now.AddMinutes(11);
        await Assert.ThrowsAsync<BusinessRuleException>(() => links.ClaimAsync(expired.BotUrl.Split("link_")[1], 12345, 12345, null, "Old", default));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Guard_RequiresCorrectBrowserAndScannerSecrets_AndSatisfiesOneSelectedFactor(bool twoFactor)
    {
        await using var db = TelegramTestData.Memory(); var user = await TelegramTestData.UserAsync(db); var clock = new TelegramTestData.Clock();
        if (twoFactor) { user.EnableEmailTwoFactor(); await db.SaveChangesAsync(); }
        await TelegramTestData.BindAsync(db, user, clock); var links = new TelegramLinkService(db, TelegramTestData.Options, clock);
        var guard = new TelegramGuardService(db, links, TelegramTestData.Identity(db), TelegramTestData.Options, clock);
        var start = await guard.CreateAsync("Test browser", "127.0.0.1", default);
        Assert.DoesNotContain(start.BrowserSecret, start.QrPayload); Assert.StartsWith("data:image/png;base64,", start.QrCodeDataUrl);
        Assert.Equal("waiting", (await guard.StatusAsync(start.Id, start.BrowserSecret, default)).Status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.CompleteAsync(start.Id, start.BrowserSecret, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.StatusAsync(start.Id, "wrong", default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => guard.PreviewAsync(start.QrPayload[..^43] + start.BrowserSecret, user.Id, default));
        Assert.Equal(start.DisplayCode, (await guard.PreviewAsync(start.QrPayload, user.Id, default)).DisplayCode);
        await guard.ApproveAsync(start.QrPayload, user.Id, 12345, default);
        Assert.Equal("approved", (await guard.StatusAsync(start.Id, start.BrowserSecret, default)).Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => guard.ApproveAsync(start.QrPayload, user.Id, 12345, default));
        var result = await guard.CompleteAsync(start.Id, start.BrowserSecret, default);
        Assert.NotNull(result.Authentication); Assert.Null(result.Challenge); Assert.Equal(user.Id.ToString(), new JwtSecurityTokenHandler().ReadJwtToken(result.Authentication.AccessToken).Subject); Assert.NotNull(user.RefreshToken);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.CompleteAsync(start.Id, start.BrowserSecret, default));
        Assert.Equal("consumed", (await guard.StatusAsync(start.Id, start.BrowserSecret, default)).Status);
    }

    [Theory]
    [InlineData("expired")] [InlineData("unlinked")] [InlineData("blocked")] [InlineData("role-changed")]
    public async Task Guard_RejectsChangedAccessBeforeCompleting(string change)
    {
        await using var db = TelegramTestData.Memory(); var user = await TelegramTestData.UserAsync(db); var clock = new TelegramTestData.Clock();
        await TelegramTestData.BindAsync(db, user, clock); var links = new TelegramLinkService(db, TelegramTestData.Options, clock);
        var guard = new TelegramGuardService(db, links, TelegramTestData.Identity(db), TelegramTestData.Options, clock);
        var start = await guard.CreateAsync("Test", null, default); await guard.ApproveAsync(start.QrPayload, user.Id, 12345, default);
        if (change == "expired") clock.Now = clock.Now.AddMinutes(3);
        else if (change == "unlinked") await links.UnlinkAsync(user.Id, default);
        else if (change == "blocked") { user.Block(); await db.SaveChangesAsync(); }
        else { var role = Role.Create(Roles.Customer, "Customer"); db.Roles.Add(role); user.AssignRole(role.Id); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guard.CompleteAsync(start.Id, start.BrowserSecret, default));
        Assert.Null(user.RefreshToken);
    }

    [Fact]
    public async Task CategoryPermissions_IncludeDescendants_AndRejectInactiveAncestorsAndSeller()
    {
        await using var db = TelegramTestData.Memory(); var seed = await TelegramTestData.SeedAsync(db); var access = new SellerCategoryAccess(db);
        Assert.Empty(await access.AllowedIdsAsync(seed.User.Id, default)); await TelegramTestData.AllowImportAsync(db, seed);
        Assert.Contains(seed.Category.Id, await access.AllowedIdsAsync(seed.User.Id, default));
        var grant = await db.SellerCategoryGrants.SingleAsync(); grant.IncludeDescendants = false; await db.SaveChangesAsync();
        Assert.DoesNotContain(seed.Category.Id, await access.AllowedIdsAsync(seed.User.Id, default));
        grant.IncludeDescendants = true; seed.Root.IsActive = false; await db.SaveChangesAsync(); Assert.Empty(await access.AllowedIdsAsync(seed.User.Id, default));
        seed.Root.IsActive = true; seed.Seller.Status = SellerStatus.SUSPENDED; await db.SaveChangesAsync(); Assert.Empty(await access.AllowedIdsAsync(seed.User.Id, default));
    }

    [Fact]
    public async Task Import_PreviewDoesNotCreateProducts_RechecksGrant_ThenSubmitsOnceWithPhotos()
    {
        await using var db = TelegramTestData.Memory(); var seed = await TelegramTestData.SeedAsync(db); var clock = new TelegramTestData.Clock();
        var source = new TelegramTestData.Source(); var media = new TelegramTestData.Media(); var importer = TelegramTestData.Import(db, clock, source, media);
        await Assert.ThrowsAsync<BusinessRuleException>(() => importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default));
        await TelegramTestData.AllowImportAsync(db, seed);
        var preview = await importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default);
        Assert.Equal("Test Phone", preview.Name); Assert.DoesNotContain("bad()", preview.Description); Assert.Single(preview.Images); Assert.Single(preview.Characteristics);
        Assert.Equal(199.90m, preview.SourcePrice); Assert.Empty(await db.Products.ToListAsync()); Assert.Equal(0, source.Downloads);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => importer.GetAsync(Guid.NewGuid(), preview.Id, default));
        var grant = await db.SellerCategoryGrants.SingleAsync(); db.Remove(grant); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => importer.SubmitAsync(seed.User.Id, preview.Id, TelegramTestData.Submit(seed), default));
        Assert.Empty(await db.Products.ToListAsync()); db.Add(grant); await db.SaveChangesAsync();
        var productId = await importer.SubmitAsync(seed.User.Id, preview.Id, TelegramTestData.Submit(seed), default);
        Assert.Equal(productId, await importer.SubmitAsync(seed.User.Id, preview.Id, TelegramTestData.Submit(seed), default));
        var product = Assert.Single(await db.Products.ToListAsync()); Assert.Equal(ProductStatus.PENDING_MODERATION, product.Status);
        Assert.Equal(seed.Category.Id, product.CategoryId); Assert.Equal(250, (await db.ProductVariants.SingleAsync()).Price);
        Assert.Single(await db.ProductImages.ToListAsync()); Assert.Single(media.Files); Assert.Equal(1, source.Downloads);
        Assert.Equal(productId, (await importer.PreviewAsync(seed.User.Id, TelegramTestData.SourceUrl, default)).ProductId);
        Assert.Single(await db.Products.ToListAsync());
    }

    [Theory]
    [InlineData("http://rozetka.com.ua/test/p123/")] [InlineData("https://evil.example/test/p123/")]
    [InlineData("https://rozetka.com.ua.evil.example/test/p123/")] [InlineData("https://rozetka.com.ua:444/test/p123/")]
    [InlineData("https://user:pass@rozetka.com.ua/test/p123/")] [InlineData("https://rozetka.com.ua/category/c123/")]
    public void Import_RejectsOtherOriginsAndNonProductUrls(string url) => Assert.Throws<ArgumentException>(() => MarketplaceSourceClient.ProductUri(url));

    [Theory]
    [InlineData("127.0.0.1")] [InlineData("169.254.169.254")] [InlineData("10.0.0.1")] [InlineData("172.16.0.1")]
    [InlineData("100.92.44.73")] [InlineData("::1")] [InlineData("::ffff:127.0.0.1")] [InlineData("fc00::1")]
    public void Import_RejectsPrivateNetworkAddresses(string address) => Assert.False(MarketplaceSourceClient.IsPublicAddress(IPAddress.Parse(address)));
}
