using System.Net;
using System.Net.Http.Json;
using Application.Catalog;
using Application.Marketplace;
using Contracts.Catalog;
using Contracts.Marketplace;
using Domain.Entities;
using Domain.Entities.Support;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests;

public sealed class MarketplaceWorkflowTests
{
    [Fact]
    public async Task OrderAndSupportReads_DoNotActivatePaymentProvider()
    {
        await using var db = TelegramTestData.Memory();
        var services = new ServiceCollection();
        services.AddSingleton<Application.Abstractions.IApplicationDbContext>(db);
        services.AddSingleton<Application.Abstractions.ICurrentActor>(new OperationsTests.Actor(Guid.NewGuid(), "Manager"));
        services.AddScoped<Application.Inventory.InventoryService>();
        services.AddScoped<Application.Promotions.IDiscountService, Application.Promotions.DiscountService>();
        var paymentActivations = 0;
        services.AddScoped<Application.Payments.IPaymentService>(_ => { paymentActivations++; throw new InvalidOperationException("No payment provider in this read-only fixture."); });
        Application.Orders.OrderRegistration.AddOrderServices(services);
        services.AddScoped<Application.Administration.SupportService>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<Application.Orders.OrderService>();
        Assert.Empty((await orders.ListAsync(1, 20, null, null, null, null, null, default)).Content);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Application.Administration.SupportService>());
        Assert.Equal(0, paymentActivations);
    }
    internal static SellerApplicationInput Input() => new() { CompanyName = "Fixture company", StoreName = "Fixture store", TaxNumber = "APP-" + Guid.NewGuid().ToString("N"),
        ContactName = "Customer Fixture", Position = "Owner", Email = "customer@example.test", Phone = "+380991234567", Website = "https://example.test" };

    [Fact]
    public async Task SellerApplication_PersistsScopesAndApprovesExistingCustomerOnce()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var service = new SellerApplicationService(db, new OperationsTests.Actor(seed.Customer.Id));
        var input = Input(); var row = await service.ApplyAsync(input, default);
        Assert.Equal("pending", (await service.MineAsync(default))!.Status);
        Assert.Null(await new SellerApplicationService(db, new OperationsTests.Actor(seed.Seller.User.Id, "Seller")).MineAsync(default));
        await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(() => service.ApplyAsync(input, default));
        var staff = new SellerApplicationService(db, new OperationsTests.Actor(seed.Seller.User.Id, "Administrator"));
        var approved = await staff.ReviewAsync(row.Id, new(row.Version, true, "Verified", "fixture-store"), default);
        Assert.Equal("approved", approved.Status);
        var seller = await db.Sellers.SingleAsync(x => x.UserId == seed.Customer.Id);
        var store = await db.Stores.SingleAsync(x => x.SellerId == seller.Id);
        Assert.Equal("fixture-store", store.Slug); Assert.True(store.IsActive);
        Assert.Equal(SellerStatus.ACTIVE, seller.Status);
        Assert.Equal((await db.Roles.SingleAsync(x => x.Name == "Seller")).Id, seed.Customer.RoleId);
        Assert.Null(seed.Customer.RefreshToken);
        Assert.NotEmpty(await db.SellerCategoryGrants.Where(x => x.SellerId == seller.Id).ToListAsync());
        await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(() => staff.ReviewAsync(row.Id, new(row.Version, true, null, "another-store"), default));
        Assert.Single(await db.Stores.Where(x => x.SellerId == seller.Id).ToListAsync());
    }

    [Fact]
    public async Task Rejection_RequiresReason_AllowsCorrection_AndNeverChangesStaffRoles()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var actor = new OperationsTests.Actor(seed.Customer.Id); var service = new SellerApplicationService(db, actor);
        var row = await service.ApplyAsync(Input(), default);
        var staff = new SellerApplicationService(db, new OperationsTests.Actor(seed.Seller.User.Id, "Manager"));
        await Assert.ThrowsAsync<ArgumentException>(() => staff.ReviewAsync(row.Id, new(row.Version, false, "", "fixture-shop"), default));
        await staff.ReviewAsync(row.Id, new(row.Version, false, "Correct company details", "fixture-shop"), default);
        var corrected = await service.ApplyAsync(Input(), default);
        Assert.Equal(row.Id, corrected.Id); Assert.Equal("pending", corrected.Status); Assert.NotEqual(row.Version, corrected.Version);
        Assert.Null(corrected.ReviewNote);
        await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(() => new SellerApplicationService(db, new OperationsTests.Actor(seed.Seller.User.Id, "Seller")).ApplyAsync(Input(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReviewAsync(row.Id, new(corrected.Version, true, null, "evil-shop"), default));
        Assert.Equal((await db.Roles.SingleAsync(x => x.Name == "Customer")).Id, seed.Customer.RoleId);
    }

    [Fact]
    public async Task ApprovalConflict_DoesNotCreateSellerOrChangeCustomer()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var row = await new SellerApplicationService(db, new OperationsTests.Actor(seed.Customer.Id)).ApplyAsync(Input(), default);
        var staff = new SellerApplicationService(db, new OperationsTests.Actor(seed.Seller.User.Id, "Manager"));
        await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(() => staff.ReviewAsync(row.Id, new(row.Version, true, null, seed.Seller.Store.Slug), default));
        Assert.False(await db.Sellers.AnyAsync(x => x.UserId == seed.Customer.Id));
        Assert.Equal("pending", (await db.SellerApplications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Preferences_SaveCoreAndDeliveryData_RejectStaleWritesAndRoleFields()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var account = new CustomerAccountService(db, new OperationsTests.Actor(seed.Customer.Id));
        var before = await account.PreferencesAsync(default);
        var updated = await account.SavePreferencesAsync(new(new() { ["firstName"] = "Changed", ["addressCity"] = "Kyiv", ["birthDate"] = "12.03.1990", ["recipientPhone"] = "+380991234567" }, before.Version), default);
        Assert.Equal("Changed", updated.Values["firstName"]); Assert.Equal("Kyiv", updated.Values["addressCity"]);
        db.ChangeTracker.Clear();
        var reloaded = await new CustomerAccountService(db, new OperationsTests.Actor(seed.Customer.Id)).PreferencesAsync(default);
        Assert.Equal(updated.Version, reloaded.Version); Assert.Equal("Kyiv", reloaded.Values["addressCity"]);
        Assert.Equal("Changed", (await db.Users.SingleAsync(x => x.Id == seed.Customer.Id)).FirstName);
        await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(() => account.SavePreferencesAsync(new(new() { ["addressCity"] = "Lost update" }, before.Version), default));
        await Assert.ThrowsAsync<ArgumentException>(() => account.SavePreferencesAsync(new(new() { ["role"] = "Administrator" }, updated.Version), default));
        await Assert.ThrowsAsync<ArgumentException>(() => account.SavePreferencesAsync(new(new() { ["recipientPhone"] = "not a phone" }, updated.Version), default));
        var other = await new CustomerAccountService(db, new OperationsTests.Actor(seed.Seller.User.Id)).PreferencesAsync(default);
        Assert.False(other.Values.ContainsKey("addressCity"));
    }

    [Fact]
    public async Task CustomerOrders_AreOwnedPaginatedAndHideInternalHistoryNotes()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var order = await OperationsTests.Service(db, new(seed.Customer.Id)).CreateOrderAsync(OperationsTests.Request(seed));
        var history = await db.OrderStatusHistories.SingleAsync(); history.Comment = "Private staff note"; await db.SaveChangesAsync();
        var account = new CustomerAccountService(db, new OperationsTests.Actor(seed.Customer.Id));
        var page = await account.OrdersAsync(1, order.OrderNumber, null, default);
        var detail = Assert.Single(page.Content); Assert.Equal(1, page.TotalElements); Assert.Single(detail.Items); Assert.Single(detail.History);
        Assert.Equal(order.TotalAmount, detail.TotalAmount); Assert.Equal(seed.Product.Slug, detail.Items[0].ProductSlug);
        Assert.DoesNotContain("Private staff note", System.Text.Json.JsonSerializer.Serialize(detail));
        var other = new CustomerAccountService(db, new OperationsTests.Actor(seed.Seller.User.Id, "Administrator"));
        Assert.Null(await other.OrderAsync(order.Id, default)); Assert.Empty((await other.OrdersAsync(1, null, null, default)).Content);
        Assert.Empty((await account.OrdersAsync(2, null, null, default)).Content);
        Assert.Empty((await account.OrdersAsync(1, null, DateTimeOffset.UtcNow.AddDays(1), default)).Content);
    }

    [Fact]
    public async Task Notifications_ScopeSellerQuestionsAndStaffSupport_HideInternalMessages_AndPersistReads()
    {
        await using var db = TelegramTestData.Memory(); var first = await OperationsTests.SeedAsync(db); var other = await OperationsTests.SeedAsync(db);
        async Task<Guid> Message(Guid? product, string subject, bool internalMessage = false, string? kind = null)
        {
            var ticket = new SupportTicket { CustomerId = first.Customer.Id, ProductId = product, Subject = subject, Kind = kind ?? (product.HasValue ? "QUESTION" : "CHAT") };
            var message = new SupportMessage { TicketId = ticket.Id, AuthorId = first.Customer.Id, AuthorRole = "Customer", AuthorName = "Customer", Message = subject, Internal = internalMessage };
            db.SupportTickets.Add(ticket); db.SupportMessages.Add(message); await db.SaveChangesAsync(); return message.Id;
        }
        var own = await Message(first.Product.Id, "Own product question"); var hidden = await Message(other.Product.Id, "Other seller private question");
        var personal = await Message(null, "Personal support"); var internalId = await Message(first.Product.Id, "Private note", true);
        var orderQuestion = await Message(null, "Order question", kind: "QUESTION");
        var seller = new NotificationService(db, new OperationsTests.Actor(first.Seller.User.Id, "Seller"));
        var feed = await seller.FeedAsync(default);
        Assert.Contains(feed.Items, x => x.Key == "message:" + own && !x.Read && x.Path.StartsWith("/support/"));
        Assert.DoesNotContain(feed.Items, x => x.Key == "message:" + hidden || x.Key == "message:" + personal || x.Key == "message:" + internalId);
        var staffFeed = await new NotificationService(db, new OperationsTests.Actor(other.Customer.Id, "Moderator")).FeedAsync(default);
        Assert.Contains(staffFeed.Items, x => x.Key == "message:" + personal);
        Assert.DoesNotContain(staffFeed.Items, x => x.Key == "message:" + own || x.Key == "message:" + hidden);
        Assert.DoesNotContain(staffFeed.Items, x => x.Key == "message:" + orderQuestion);
        Assert.Contains((await new NotificationService(db, new OperationsTests.Actor(other.Customer.Id, "Manager")).FeedAsync(default)).Items, x => x.Key == "message:" + orderQuestion);
        await seller.ReadAsync(["message:" + own], default); await seller.ReadAsync(["message:" + own], default);
        Assert.True((await seller.FeedAsync(default)).Items.Single(x => x.Key == "message:" + own).Read);
        Assert.Single(await db.NotificationReceipts.Where(x => x.EventKey == "message:" + own).ToListAsync());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new NotificationService(db, new OperationsTests.Actor(other.Seller.User.Id, "Seller")).ReadAsync(["message:" + own], default));
    }

    [Fact]
    public async Task PublicCatalog_FiltersPriceBrandAndAttributes_AndDoesNotExposeDraftFacets()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db); var other = await OperationsTests.SeedAsync(db);
        seed.Product.Name = "Search fixture"; other.Product.Name = "Search fixture draft"; other.Product.Status = ProductStatus.DRAFT;
        var attribute = new Domain.Entities.Attribute.Attribute(Guid.NewGuid(), seed.Product.CategoryId, "Memory", "memory", AttributeType.NUMBER, false, true, false, "GB", 0);
        db.Attributes.Add(attribute); db.ProductAttributeValues.Add(new() { Id = Guid.NewGuid(), ProductId = seed.Product.Id, AttributeId = attribute.Id, NumberValue = 16 });
        await db.SaveChangesAsync(); var catalog = new CatalogService(db);
        var filtered = await catalog.ListAsync(1, 24, "Search fixture", null, true, null, "priceasc", default, filters: new() { BrandId = seed.Product.BrandId, MinPrice = 50, MaxPrice = 150, Attributes = new() { [attribute.Id] = "16" } });
        Assert.Equal(seed.Product.Id, Assert.Single(filtered.Content).Id);
        var empty = await catalog.ListAsync(1, 24, null, null, true, null, null, default, filters: new() { Attributes = new() { [attribute.Id] = "32" } }); Assert.Empty(empty.Content);
        var facets = await catalog.PublicFacetsAsync(null, "Search fixture", default);
        Assert.DoesNotContain(facets.Brands, x => x.Id == other.Product.BrandId);
        Assert.Equal("16", Assert.Single(Assert.Single(facets.Attributes).Options).Value);
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.ListAsync(1, 24, null, null, true, null, null, default, filters: new() { MinPrice = 200, MaxPrice = 100 }));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)] [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Seller", HttpStatusCode.Forbidden)] [InlineData("Moderator", HttpStatusCode.Forbidden)]
    [InlineData("Manager", HttpStatusCode.OK)] [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task SellerApplicationAdminRoutes_EnforceReviewRoles(string? role, HttpStatusCode expected)
    {
        await using var server = await AdminHttpTests.ServerAsync(); using var client = server.GetTestClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(expected, (await client.GetAsync("/api/v1/admin/seller-applications")).StatusCode);
    }

    [Fact]
    public async Task PublicCatalogHttp_BindsAttributesUnderTheirPrefix_AndAcceptsOrdinaryQueryParameters()
    {
        await using var server = await AdminHttpTests.ServerAsync(); using var client = server.GetTestClient();
        OperationsTests.Seed seed; Guid attributeId;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seed = await OperationsTests.SeedAsync(db);
            attributeId = Guid.NewGuid();
            db.Attributes.Add(new Domain.Entities.Attribute.Attribute(attributeId, seed.Product.CategoryId, "Memory", "memory", AttributeType.NUMBER, false, true, false, "GB", 0));
            db.ProductAttributeValues.Add(new() { Id = Guid.NewGuid(), ProductId = seed.Product.Id, AttributeId = attributeId, NumberValue = 16 });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/catalog?size=24&sort=priceasc")).StatusCode);
        var matches = await client.GetAsync($"/api/v1/catalog?size=24&search=Product&attributes[{attributeId}]=16");
        Assert.Equal(HttpStatusCode.OK, matches.StatusCode);
        var matchJson = await matches.Content.ReadAsStringAsync();
        Assert.Contains(seed.Product.Id.ToString(), matchJson);
        var misses = await client.GetAsync($"/api/v1/catalog?size=24&attributes[{attributeId}]=32");
        Assert.Equal(HttpStatusCode.OK, misses.StatusCode);
        Assert.DoesNotContain(seed.Product.Id.ToString(), await misses.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CustomerHttpRoutes_UseSignedInOwnerAndDoNotExposeOtherOrders()
    {
        await using var server = await AdminHttpTests.ServerAsync(); using var client = server.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/account/orders")).StatusCode);
        OperationsTests.Seed seed;
        using (var scope = server.Services.CreateScope()) seed = await OperationsTests.SeedAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Customer"); client.DefaultRequestHeaders.Add("X-Test-UserId", seed.Customer.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/account/preferences")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/account/orders/" + Guid.NewGuid())).StatusCode);
        var applied = await client.PostAsJsonAsync("/api/v1/account/seller-application", Input()); Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var mine = await client.GetStringAsync("/api/v1/account/seller-application"); Assert.Contains("Fixture company", mine);
    }
}
