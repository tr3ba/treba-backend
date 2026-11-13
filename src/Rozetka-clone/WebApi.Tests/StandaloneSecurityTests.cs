using Application.Catalog;
using Application.Inventory;
using Domain.Entities.Product;
using Domain.Entities.Users;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using ImageMagick;
using WebApi.Media;
using WebApi.Telegram;
using Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Application.Abstractions;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace WebApi.Tests;

public sealed class StandaloneSecurityTests
{
    private static readonly IOptions<SecurityOptions> Security = Options.Create(new SecurityOptions
    {
        EncryptionKey = Convert.ToBase64String(new byte[32]),
        OtpHashKey = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray())
    });

    private sealed class Mail : IEmailSender
    {
        public Dictionary<string, string> Codes = [];
        public Task SendSecurityCodeAsync(string recipient, string displayName, string code, TimeSpan validFor, CancellationToken ct = default)
        { Codes[recipient] = code; return Task.CompletedTask; }
    }

    private sealed class Photo : IProductMediaStorage
    {
        public int Reads;
        public Task SaveAsync(Guid productId, string fileName, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Guid productId, string fileName, CancellationToken ct) => Task.CompletedTask;
        public Task<ProductMediaFile?> ReadAsync(Guid productId, string fileName, CancellationToken ct)
        { Reads++; return Task.FromResult<ProductMediaFile?>(new([1], "image/png")); }
    }

    [Fact]
    public async Task KnownPhotoUrl_FollowsVisibility_WhileAnOwnerTicketStillLoadsADraft()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await OperationsTests.SeedAsync(db);
        var clock = new TelegramTestData.Clock();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, seed.Seller.User.Id.ToString())], "test"));
        var urls = new ProductMediaUrls(http, Security, clock);
        var media = new Photo();
        var controller = new WebApi.Controllers.ProductMediaController(media, db, new OperationsTests.Actor(null), null!, urls)
        { ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new DefaultHttpContext() } };
        var name = Guid.NewGuid().ToString("N") + ".png";
        db.ProductImages.Add(new ProductImage
        {
            Id = Guid.NewGuid(),
            ProductId = seed.Product.Id,
            ImageUrl = $"/uploads/products/{seed.Product.Id:N}/{name}",
            SortOrder = 0,
            IsMain = true
        });
        await db.SaveChangesAsync();
        Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(await controller.Get(seed.Product.Id, name, default));
        seed.Product.Status = ProductStatus.DRAFT;
        await db.SaveChangesAsync();
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.Get(seed.Product.Id, name, default));
        Assert.Equal(1, media.Reads);
        var token = urls.ForCurrentActor($"/uploads/products/{seed.Product.Id:N}/{name}").Split("?access=")[1];
        Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(await controller.Get(seed.Product.Id, name, default, token));
        Assert.Equal("private,no-store", controller.Response.Headers.CacheControl);
        seed.Seller.Seller.Status = SellerStatus.SUSPENDED;
        await db.SaveChangesAsync();
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await controller.Get(seed.Product.Id, name, default, token));
    }

    private sealed class DeliveryDiscount : Application.Promotions.IDiscountService
    {
        public bool Free = true;
        public Task<Application.Promotions.DiscountResult> CalculateDiscountAsync(Guid userId,
            IReadOnlyCollection<Application.Promotions.PriceItem> items, string? coupon, CancellationToken ct = default) =>
            Task.FromResult(new Application.Promotions.DiscountResult(200, 0, 200, Free, null, null));
        public Task<Application.Promotions.CouponValidationResult> ValidateCouponAsync(Guid userId, string coupon, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RegisterCouponUsageAsync(Guid couponId, Guid userId, Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Checkout_RejectsAFreeQuoteAfterThePromotionConditionsChange()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await OperationsTests.SeedAsync(db);
        var actor = new OperationsTests.Actor(seed.Customer.Id);
        var discounts = new DeliveryDiscount();
        var clock = new TelegramTestData.Clock();
        var delivery = new Application.Delivery.CheckoutDeliveryService(db, new DeliveryAndNotificationTests.Provider(), discounts, actor, clock);
        List<Contracts.Delivery.DeliveryQuoteItem> items = [new(seed.Variant.Id, 2)];
        var quote = await delivery.QuoteAsync(new(DeliveryAndNotificationTests.CityId, DeliveryAndNotificationTests.PointId, items, "FREE"), default);
        Assert.Equal(0, quote.Price);
        Assert.NotNull(await delivery.ValidateAsync(quote.Id, seed.Customer.Id, items, "FREE", 200, 200, default));
        discounts.Free = false;
        await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(() => delivery.ValidateAsync(quote.Id, seed.Customer.Id, items, "FREE", 200, 200, default));
        var renewed = await delivery.QuoteAsync(new(DeliveryAndNotificationTests.CityId, DeliveryAndNotificationTests.PointId, items, "FREE"), default);
        Assert.Equal(95.50m, renewed.Price);
        Assert.NotNull(await delivery.ValidateAsync(renewed.Id, seed.Customer.Id, items, "FREE", 200, 200, default));
    }

    [Fact]
    public async Task MixedOrders_RedactSharedContactsAndCannotSearchThem_StaffRetainsAccess()
    {
        await using var db = TelegramTestData.Memory();
        var first = await OperationsTests.SeedAsync(db);
        var second = await OperationsTests.SeedAsync(db);
        var customer = new OperationsTests.Actor(first.Customer.Id);
        var request = OperationsTests.Request(first) with
        {
            Items = [OperationsTests.Request(first).Items![0], OperationsTests.Request(second).Items![0]]
        };
        var order = await OperationsTests.Service(db, customer).CreateOrderAsync(request);
        var seller = new OperationsTests.Actor(first.Seller.User.Id, "Seller");
        var service = OperationsTests.Service(db, seller);
        var details = (await service.DetailsAsync(order.Id, default))!;
        Assert.True(details.PartialOrder);
        Assert.Empty(details.RecipientEmail);
        Assert.Empty(details.RecipientPhone);
        Assert.Null(details.Address);
        Assert.Single(details.Items);
        Assert.Empty((await service.ListAsync(1, 12, null, request.RecipientEmail, null, null, null, default)).Content);
        var staff = OperationsTests.Service(db, new OperationsTests.Actor(first.Seller.User.Id, "Manager"));
        var full = (await staff.DetailsAsync(order.Id, default))!;
        Assert.Equal(request.RecipientEmail, full.RecipientEmail);
        Assert.NotNull(full.Address);
    }

    [Fact]
    public async Task CustomerDetails_RejectStaffWithoutCustomerHistory()
    {
        await using var db = TelegramTestData.Memory();
        var staff = await TelegramTestData.UserAsync(db, "Manager");
        var actor = new OperationsTests.Actor(staff.Id, "Manager");
        var operations = new Application.Administration.OperationsService(db);
        Assert.Null(await operations.CustomerAsync(staff.Id, default));
    }

    [Fact]
    public async Task QuoteCleanup_RemovesExpiredUnusedQuotes_AndKeepsOrdersAndCurrentQuotes()
    {
        await using var db = TelegramTestData.Memory();
        var now = DateTimeOffset.UtcNow;
        var expired = new Domain.Entities.Orders.DeliveryQuote { ExpiresAt = now.AddDays(-2) };
        var current = new Domain.Entities.Orders.DeliveryQuote { ExpiresAt = now.AddMinutes(5) };
        var used = new Domain.Entities.Orders.DeliveryQuote { ExpiresAt = now.AddDays(-2), UsedByOrderId = Guid.NewGuid() };
        db.DeliveryQuotes.AddRange(expired, current, used);
        await db.SaveChangesAsync();
        Assert.Equal(1, await WebApi.Delivery.DeliveryQuoteCleanupWorker.CleanupAsync(db, now, default));
        Assert.False(await db.DeliveryQuotes.AnyAsync(q => q.Id == expired.Id));
        Assert.Equal(2, await db.DeliveryQuotes.CountAsync());
    }

    [Fact]
    public async Task EmailChange_RequiresCurrentMfaAndBothMailboxes_PreservesFactorAndRevokesSession()
    {
        await using var db = TelegramTestData.Memory();
        var user = await TelegramTestData.UserAsync(db, "Customer");
        user.EnableEmailTwoFactor();
        user.SetRefreshToken("current-session", DateTimeOffset.UtcNow.AddDays(1));
        await db.SaveChangesAsync();
        var original = user.Email;
        var mail = new Mail();
        var factors = new SecondFactorService(db, mail, new(Security), Security);
        var changes = new AccountChangeService(db, factors, mail, Security);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => changes.BeginAsync(user.Id, new("new@example.test"), true, default));
        var login = (await factors.CreateLoginChallengeAsync(user))!;
        await factors.SendLoginEmailCodeAsync(login.ChallengeId);
        var change = await changes.BeginAsync(user.Id, new("new@example.test", new(login.ChallengeId, "email", mail.Codes[original])), true, default);
        Assert.Equal(original, user.Email);
        Assert.True(user.EmailTwoFactorEnabled);
        var wrongCode = mail.Codes[original] == "000000" ? "111111" : "000000";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => changes.ConfirmAsync(user.Id, change.ChallengeId, wrongCode, mail.Codes["new@example.test"], true, null, default));
        Assert.Equal(original, user.Email);
        await changes.ConfirmAsync(user.Id, change.ChallengeId, mail.Codes[original], mail.Codes["new@example.test"], true, null, default);
        Assert.Equal("new@example.test", user.Email);
        Assert.True(user.EmailVerified);
        Assert.True(user.EmailTwoFactorEnabled);
        Assert.Null(user.RefreshToken);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => changes.ConfirmAsync(user.Id, change.ChallengeId, mail.Codes[original], mail.Codes["new@example.test"], true, null, default));
    }

    [Fact]
    public void PrivatePhotoTickets_AreBoundToPathAndTime()
    {
        var userId = Guid.NewGuid();
        var clock = new TelegramTestData.Clock();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));
        var urls = new ProductMediaUrls(http, Security, clock);
        var productId = Guid.NewGuid();
        var name = Guid.NewGuid().ToString("N") + ".jpg";
        var url = urls.ForCurrentActor($"/uploads/products/{productId:N}/{name}");
        var token = url.Split("?access=")[1];
        Assert.Equal(userId, urls.Validate(productId, name, token));
        Assert.Null(urls.Validate(Guid.NewGuid(), name, token));
        Assert.Null(urls.Validate(productId, "another.jpg", token));
        clock.Now = clock.Now.AddMinutes(6);
        Assert.Null(urls.Validate(productId, name, token));
    }

    [Fact]
    public async Task RetryCannotExceedTheSameFiftyJobLimitAsAdd()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await TelegramTestData.SeedAsync(db);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 50; i++)
            db.SellerImportJobs.Add(new() { UserId = seed.User.Id, SourceUrl = $"https://shop.example.test/{i}", CreatedAt = now, UpdatedAt = now, Status = "Queued" });
        var failed = new Domain.Entities.Telegram.SellerImportJob { UserId = seed.User.Id, SourceUrl = "https://shop.example.test/failed", Status = "Failed" };
        db.SellerImportJobs.Add(failed);
        await db.SaveChangesAsync();
        var queue = new SellerImportQueue(db, new SellerCategoryAccess(db), new TelegramTestData.Clock());
        await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(() => queue.RetryAsync(seed.User.Id, failed.Id, default));
        Assert.Equal("Failed", failed.Status);
        db.SellerImportJobs.Remove(await db.SellerImportJobs.FirstAsync(j => j.Status == "Queued"));
        await db.SaveChangesAsync();
        await queue.RetryAsync(seed.User.Id, failed.Id, default);
        Assert.Equal("Queued", failed.Status);
        Assert.Equal(50, await db.SellerImportJobs.CountAsync(j => j.Status == "Queued"));
    }

    [Theory]
    [InlineData("//tracker.example.test/pixel")]
    [InlineData("http://tracker.example.test/pixel")]
    [InlineData("data:image/svg+xml,tracker")]
    public void ProductPhotoUrls_NeverReturnExternalSourcesToBrowsers(string source)
    {
        var id = Guid.NewGuid();
        Assert.StartsWith($"/uploads/products/{id:N}/external-", Application.ProductImages.ProductImageUrls.ForBrowser(id, Guid.NewGuid(), source));
        Assert.Throws<ArgumentException>(() => Application.ProductImages.ProductImageUrls.Validate(id, source));
    }

    [Fact]
    public async Task PublicCatalogAndInventory_HideSuspendedSellersAndInactiveVariants()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await CatalogTestData.SeedAsync(db);
        var catalog = new CatalogService(db);
        var product = (await catalog.SaveAsync(null, seed.Store.Id, CatalogTestData.Request(seed), default))!;
        (await db.Products.SingleAsync(p => p.Id == product.Id)).Status = ProductStatus.ACTIVE;
        var hidden = new ProductVariant(Guid.NewGuid(), product.Id, "hidden-private-sku", null, "hidden", 42, null, null, null, null, null, null, false);
        db.ProductVariants.Add(hidden);
        await db.SaveChangesAsync();
        var visible = await catalog.GetBySlugAsync(product.Slug, true, default);
        Assert.NotNull(visible);
        Assert.DoesNotContain(visible.Variants, v => v.Id == hidden.Id);
        var stock = new InventoryService(db, new CatalogTestData.Actor(null));
        Assert.Null(await stock.GetInventoryAsync(hidden.Id));
        seed.Seller.Status = SellerStatus.SUSPENDED;
        await db.SaveChangesAsync();
        Assert.Null(await catalog.GetBySlugAsync(product.Slug, true, default));
        Assert.Empty((await catalog.ListAsync(1, 20, null, null, true, null, null, default)).Content);
        Assert.Null(await stock.GetInventoryAsync(product.Variants.First().Id));
        Assert.False(await catalog.DeleteAsync(product.Id, seed.Seller.UserId, default));
        seed.Seller.Status = SellerStatus.ACTIVE;
        await db.SaveChangesAsync();
        Assert.NotNull(await catalog.GetBySlugAsync(product.Slug, true, default));
    }

    [Fact]
    public void EmailCodeResend_PreservesFailuresAndOriginalDeadline_AndLimitsSends()
    {
        var now = DateTimeOffset.UtcNow;
        var challenge = AuthenticationChallenge.Create(Guid.NewGuid(), AuthenticationChallengePurpose.Login, now.AddMinutes(5));
        challenge.SetCode("first", now.AddMinutes(5), now);
        for (var i = 0; i < 4; i++) challenge.RegisterFailure();
        challenge.SetCode("second", now.AddMinutes(10), now.AddSeconds(31));
        Assert.Equal(4, challenge.FailedAttempts);
        Assert.Equal(now.AddMinutes(5), challenge.ExpiresAt);
        challenge.RegisterFailure();
        Assert.False(challenge.IsActive(now.AddSeconds(32)));
    }

    [Fact]
    public void AccountMfaBudgetAndTotpStep_SpanChallenges()
    {
        var user = User.Create(Guid.NewGuid(), "owner@example.test", null, "Owner", "Test");
        var now = DateTimeOffset.UtcNow;
        Assert.True(user.HasMfaBudget(now));
        for (var i = 0; i < 5; i++) user.RegisterMfaFailure();
        Assert.False(user.HasMfaBudget(now.AddMinutes(1)));
        Assert.True(user.HasMfaBudget(now.AddMinutes(16)));
        Assert.True(user.AcceptAuthenticatorStep(100));
        Assert.False(user.AcceptAuthenticatorStep(100));
        Assert.False(user.AcceptAuthenticatorStep(99));
        Assert.True(user.AcceptAuthenticatorStep(101));
    }

    [Fact]
    public void PublicPhotoEncoding_RemovesExif_AndPreservesPixels()
    {
        using var image = new MagickImage(MagickColors.Red, 2, 2);
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.ImageDescription, "private metadata");
        image.SetProfile(profile);
        var safe = SafeImage.Normalize(image.ToByteArray(MagickFormat.Jpeg), ".jpg");
        using var decoded = new MagickImage(safe);
        Assert.Null(decoded.GetExifProfile());
        Assert.Null(decoded.GetXmpProfile());
        Assert.Equal(2u, decoded.Width);
        using var converted = new MagickImage(SafeImage.Normalize(image.ToByteArray(MagickFormat.Jpeg), ".png"));
        Assert.Equal(MagickFormat.Png, converted.Format);
        Assert.Throws<ArgumentException>(() => SafeImage.Normalize([1, 2, 3], ".png"));
    }

    [Fact]
    public void RepeatedLargeJsonLdFragments_AreRejectedWithinTheParserBudget()
    {
        var title = new string('a', 270_000);
        var html = "<script type='application/ld+json'>{\"@graph\":[{\"@type\":\"Product\",\"name\":\"Test\",\"description\":\"" + title + "\"}]}</script>";
        Assert.Throws<Application.Common.BusinessRuleException>(() => new GenericProductParser().Parse(new("https://shop.example.test/product"), html));
    }
}

