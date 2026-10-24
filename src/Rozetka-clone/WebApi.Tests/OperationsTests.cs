using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Application.Administration;
using Application.Catalog;
using Application.Common;
using Application.Inventory;
using Application.Orders;
using Application.Products;
using Application.Promotions;
using Contracts.Admin.Operations;
using Contracts.Catalog;
using Contracts.Orders;
using Domain.Entities.Product;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests;

public sealed class OperationsTests
{
    internal sealed record Actor(Guid? UserId, string Role = "Customer") : ICurrentActor
    {
        public string Name => "Operations fixture";
    }

    internal sealed record Seed(TelegramTestData.Seed Seller, User Customer, Product Product, ProductVariant Variant);

    internal static async Task<Seed> SeedAsync(ApplicationDbContext db, string suffix = "")
    {
        var seed = await TelegramTestData.SeedAsync(db);
        var customer = await TelegramTestData.UserAsync(db, "Customer");
        var product = new Product
        {
            Id = Guid.NewGuid(),
            StoreId = seed.Store.Id,
            CategoryId = seed.Category.Id,
            BrandId = seed.Brand.Id,
            Name = "Operations product" + suffix,
            Slug = "ops-" + Guid.NewGuid().ToString("N"),
            Status = ProductStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var v = new ProductVariant(
            Guid.NewGuid(),
            product.Id,
            "OPS-" + Guid.NewGuid().ToString("N"),
            null,
            "Main",
            100,
            null,
            null,
            null,
            null,
            null,
            null,
            true
        );
        v.SetStock(10);
        db.AddRange(product, v);
        await db.SaveChangesAsync();
        return new(seed, customer, product, v);
    }

    internal static OrderService Service(ApplicationDbContext db, Actor actor) =>
    new(db, new InventoryService(db, actor), new DiscountService(db), actor, () => new TestPaymentService(db));

    internal static CreateOrderRequest Request(Seed s, int quantity = 2, string? coupon = null) =>
        new(
            s.Customer.Id,
            "Please call first",
            "Customer Fixture",
            "+380000000000",
            "fixture@example.test",
            new("Ukraine", null, "Kyiv", "Test street", "1", null, "01000", "Customer Fixture", "+380000000000"),
            [
                new(
                    s.Product.Id,
                    s.Variant.Id,
                    s.Seller.Seller.Id,
                    "Untrusted name",
                    "Untrusted variant",
                    "Untrusted SKU",
                    quantity,
                    .01m,
                    999m,
                    "not json"
                ),
            ],
            coupon
        );

    private static PromotionInput Promotion(string code = "SAVE20") =>
        new()
        {
            Name = "Fixture promotion",
            Type = "PERCENTAGE",
            Status = "ACTIVE",
            DiscountValue = 20,
            StartAt = DateTimeOffset.UtcNow.AddHours(-1),
            EndAt = DateTimeOffset.UtcNow.AddDays(1),
            Coupons =
            [
                new()
                {
                    Code = code,
                    UsageLimit = 1,
                    UsageLimitPerUser = 1,
                },
            ],
        };

    [Fact]
    public async Task Checkout_UsesServerPricesNamesAndCustomer_AndCancelRestoresStockExactlyOnce()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var service = Service(db, new(s.Customer.Id));
        var created = await service.CreateOrderAsync(Request(s));
        Assert.Equal(200, created.Subtotal);
        Assert.Equal(250, created.TotalAmount);
        Assert.Equal(0, created.DiscountAmount);
        Assert.Equal(8, s.Variant.StockQuantity);
        var line = Assert.Single(await db.OrderItems.ToListAsync());
        Assert.Equal(s.Product.Name, line.ProductName);
        Assert.Equal(100, line.UnitPrice);
        Assert.Null(line.ProductSnapshot);
        Assert.Equal(200, line.TotalPrice);
        var details = (await service.DetailsAsync(created.Id, default))!;
        await service.ChangeStatusAsync(created.Id, new("CANCELLED", details.Version, "Changed my mind"), default);
        Assert.Equal(10, s.Variant.StockQuantity);
        Assert.Equal(ReservationStatus.RELEASED, (await db.InventoryReservations.SingleAsync()).Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ChangeStatusAsync(created.Id, new("CANCELLED", details.Version, "Again"), default)
        );
        Assert.Equal(10, s.Variant.StockQuantity);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateOrderAsync(Request(s) with { CustomerId = Guid.NewGuid() })
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task InvalidStock_DoesNotPersistAnOrder(int quantity)
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var service = Service(db, new(s.Customer.Id));
        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateOrderAsync(Request(s, quantity)));
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Equal(0, await db.InventoryReservations.CountAsync());
        Assert.Equal(10, s.Variant.StockQuantity);
    }

    [Fact]
    public async Task Coupons_ApplyOnlyToEligibleItems_AndUsageIsRegisteredWithTheOrder()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var p = Promotion();
        p.ProductId = s.Product.Id;
        await new PromotionManagementService(db).SaveAsync(null, p, default);
        var service = Service(db, new(s.Customer.Id));
        var created = await service.CreateOrderAsync(Request(s, coupon: "save20"));
        Assert.Equal(40, created.DiscountAmount);
        Assert.Equal(210, created.TotalAmount);
        Assert.Equal(40, (await db.OrderItems.SingleAsync()).DiscountAmount);
        Assert.Equal(160, (await db.OrderItems.SingleAsync()).TotalPrice);
        Assert.Equal(1, (await db.Coupons.SingleAsync()).UsedCount);
        Assert.Equal(created.Id, (await db.CouponUsages.SingleAsync()).OrderId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateOrderAsync(Request(s, coupon: "SAVE20"))
        );
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(8, s.Variant.StockQuantity);
    }

    [Fact]
    public async Task StockReservation_InvalidatesAnAlreadyOpenCatalogEditor()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var catalog = new CatalogService(db);
        var before = (await catalog.GetAsync(s.Product.Id, default))!;
        await Service(db, new(s.Customer.Id)).CreateOrderAsync(Request(s));
        var stale = new SaveCatalogProductRequest
        {
            Name = before.Name,
            Slug = before.Slug,
            StoreId = before.StoreId,
            CategoryId = before.CategoryId,
            BrandId = before.BrandId,
            ExpectedUpdatedAt = before.UpdatedAt,
            Variants = before.Variants.ToList(),
            Attributes = before.Attributes.ToList(),
        };
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            catalog.SaveAsync(s.Product.Id, s.Seller.Store.Id, stale, default)
        );
        Assert.Equal(8, s.Variant.StockQuantity);
    }

    [Fact]
    public async Task PercentageAllocation_PreservesLineAndOrderTotals_WithRestrictedProducts()
    {
        await using var db = TelegramTestData.Memory();
        var first = await SeedAsync(db);
        var second = await SeedAsync(db);
        var p = Promotion();
        p.ProductId = first.Product.Id;
        p.DiscountValue = 33.33m;
        await new PromotionManagementService(db).SaveAsync(null, p, default);
        var request = Request(first, 3, "SAVE20");
        request.Items.Add(Request(second, 2).Items.Single());
        var order = await Service(db, new(first.Customer.Id)).CreateOrderAsync(request);
        Assert.Equal(99.99m, order.DiscountAmount);
        var lines = await db.OrderItems.OrderBy(i => i.ProductId).ToListAsync();
        Assert.Equal(0, lines.Single(i => i.ProductId == second.Product.Id).DiscountAmount);
        Assert.Equal(order.Subtotal - order.DiscountAmount, lines.Sum(i => i.TotalPrice));
    }

    [Fact]
    public async Task PaymentAndFulfillment_EnforceTransitionsAndFreshVersions()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var created = await Service(db, new(s.Customer.Id)).CreateOrderAsync(Request(s));
        var customer = Service(db, new(s.Customer.Id));
        var details = (await customer.DetailsAsync(created.Id, default))!;
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            customer.ChangeStatusAsync(created.Id, new("PAID", details.Version, null), default)
        );
        var staff = Service(db, new(Guid.NewGuid(), "Manager"));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            staff.ChangeStatusAsync(created.Id, new("SHIPPED", details.Version, null), default)
        );
        await staff.ChangeStatusAsync(created.Id, new("PAID", details.Version, "Received cash"), default);
        Assert.Equal(8, s.Variant.StockQuantity);
        Assert.Equal(ReservationStatus.CONFIRMED, (await db.InventoryReservations.SingleAsync()).Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            staff.ChangeStatusAsync(created.Id, new("CONFIRMED", details.Version, null), default)
        );
        foreach (
            var state in new[] { "CONFIRMED", "PROCESSING", "READY_FOR_SHIPMENT", "SHIPPED", "DELIVERED", "COMPLETED" }
        )
        {
            var d = (await staff.DetailsAsync(created.Id, default))!;
            await staff.ChangeStatusAsync(created.Id, new(state, d.Version, null), default);
        }
        Assert.Empty((await staff.DetailsAsync(created.Id, default))!.AllowedStatuses);
        Assert.Equal(8, s.Variant.StockQuantity);
    }

    [Fact]
    public async Task ExpiredUnpaidOrder_IsCancelledOnce_AndCannotBePaid()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var created = await Service(db, new(s.Customer.Id)).CreateOrderAsync(Request(s));
        var r = await db.InventoryReservations.SingleAsync();
        r.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        var staff = Service(db, new(Guid.NewGuid(), "Manager"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.PayOrderAsync(created.Id));
        await staff.ExpireReservationAsync(created.Id, default);
        await staff.ExpireReservationAsync(created.Id, default);
        Assert.Equal(10, s.Variant.StockQuantity);
        Assert.Equal(OrderStatus.CANCELLED, (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task MissingReservation_CannotSilentlyConfirmPaymentOrRestoreStock()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var order = await Service(db, new(s.Customer.Id)).CreateOrderAsync(Request(s));
        db.InventoryReservations.RemoveRange(await db.InventoryReservations.ToListAsync());
        await db.SaveChangesAsync();
        var staff = Service(db, new(Guid.NewGuid(), "Manager"));
        var details = (await staff.DetailsAsync(order.Id, default))!;
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            staff.ChangeStatusAsync(order.Id, new("PAID", details.Version, "Received"), default)
        );
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            staff.ChangeStatusAsync(order.Id, new("CANCELLED", details.Version, "Cancel"), default)
        );
        Assert.Equal(OrderStatus.AWAITING_PAYMENT, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(8, s.Variant.StockQuantity);
    }

    [Fact]
    public async Task MixedSellerOrder_HidesOtherItemsAndAmounts_AndCannotBeChangedBySeller()
    {
        await using var db = TelegramTestData.Memory();
        var first = await SeedAsync(db);
        var other = await SeedAsync(db);
        var req = Request(first);
        req.Items.Add(Request(other, 3).Items.Single());
        var order = await Service(db, new(first.Customer.Id)).CreateOrderAsync(req);
        var seller = Service(db, new(first.Seller.User.Id, "Seller"));
        var view = (await seller.DetailsAsync(order.Id, default))!;
        Assert.Single(view.Items);
        Assert.Equal(200, view.Summary.TotalAmount);
        Assert.True(view.PartialOrder);
        Assert.Empty(view.AllowedStatuses);
        Assert.Null(view.CustomerComment);
        Assert.Equal(0, view.DeliveryPrice);
        var list = await seller.ListAsync(1, 20, null, null, null, null, null, default);
        Assert.Equal(200, Assert.Single(list.Content).TotalAmount);
        Assert.Null(await Service(db, new(Guid.NewGuid(), "Seller")).DetailsAsync(order.Id, default));
        Assert.Null(await Service(db, new(Guid.NewGuid())).GetOrderByIdAsync(order.Id));
        Assert.Null(await seller.GetOrderByIdAsync(order.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            seller.ChangeStatusAsync(order.Id, new("PAID", view.Version, null), default)
        );
    }

    [Fact]
    public async Task ReturnWorkflow_IsAtomicWithOrderHistory_AndRestoresStockOnlyOnReceipt()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var created = await Service(db, new(s.Customer.Id)).CreateOrderAsync(Request(s));
        var actor = new Actor(Guid.NewGuid(), "Manager");
        var orders = Service(db, actor);
        foreach (var state in new[] { "PAID", "CONFIRMED", "PROCESSING", "READY_FOR_SHIPMENT", "SHIPPED", "DELIVERED" })
        {
            var d = (await orders.DetailsAsync(created.Id, default))!;
            await orders.ChangeStatusAsync(created.Id, new(state, d.Version, null), default);
        }
        var support = new SupportService(db, actor, orders);
        var ticket = await support.CreateAsync(
            new()
            {
                OrderId = created.Id,
                Kind = "RETURN",
                Subject = "Return order",
                Message = "Does not fit",
            },
            default
        );
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            support.CreateAsync(
                new()
                {
                    OrderId = created.Id,
                    Kind = "RETURN",
                    Subject = "Duplicate",
                    Message = "Duplicate",
                },
                default
            )
        );
        await support.ChangeStatusAsync(ticket.Summary.Id, new("APPROVED", ticket.Summary.Version), default);
        Assert.Equal(OrderStatus.RETURN_REQUESTED, (await db.Orders.SingleAsync()).Status);
        Assert.Equal(8, s.Variant.StockQuantity);
        var approved = (await support.DetailsAsync(ticket.Summary.Id, default))!;
        await support.ChangeStatusAsync(ticket.Summary.Id, new("CLOSED", approved.Summary.Version), default);
        Assert.Equal(10, s.Variant.StockQuantity);
        Assert.Equal(OrderStatus.RETURNED, (await db.Orders.SingleAsync()).Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            support.ChangeStatusAsync(ticket.Summary.Id, new("CLOSED", approved.Summary.Version), default)
        );
        Assert.Equal(10, s.Variant.StockQuantity);
    }

    [Fact]
    public async Task Support_CustomerCannotSeeOtherTicketsOrInternalNotes()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var created = await Service(db, new(s.Customer.Id)).CreateOrderAsync(Request(s));
        var customer = new Actor(s.Customer.Id);
        var support = new SupportService(db, customer, Service(db, customer));
        var ticket = await support.CreateAsync(
            new()
            {
                OrderId = created.Id,
                Subject = "Question",
                Message = "Where is my order?",
            },
            default
        );
        var staff = new Actor(Guid.NewGuid(), "Manager");
        await new SupportService(db, staff, Service(db, staff)).MessageAsync(
            ticket.Summary.Id,
            new("Private staff note", true),
            default
        );
        var details = (await support.DetailsAsync(ticket.Summary.Id, default))!;
        Assert.Single(details.Messages);
        Assert.DoesNotContain(details.Messages, m => m.Internal);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            support.MessageAsync(ticket.Summary.Id, new("Secret", true), default)
        );
        var stranger = new Actor(Guid.NewGuid());
        var other = new SupportService(db, stranger, Service(db, stranger));
        Assert.Null(await other.DetailsAsync(ticket.Summary.Id, default));
        Assert.Empty((await other.ListAsync(1, null, null, null, default)).Content);
    }

    [Fact]
    public async Task PromotionEditor_RejectsStaleAndInvalidInput_AndKeepsCouponHistory()
    {
        await using var db = TelegramTestData.Memory();
        var service = new PromotionManagementService(db);
        var input = Promotion();
        var p = await service.SaveAsync(null, input, default);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(p.Id, input, default));
        input.ExpectedVersion = p.Version;
        input.Coupons = [new() { Code = "NEWCODE" }];
        await service.SaveAsync(p.Id, input, default);
        Assert.False((await db.Coupons.SingleAsync(c => c.Code == "SAVE20")).Active);
        Assert.True((await db.Coupons.SingleAsync(c => c.Code == "NEWCODE")).Active);
        input = Promotion("OTHER");
        input.DiscountValue = 101;
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(null, input, default));
        input = Promotion("NEWCODE");
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveAsync(null, input, default));
    }

    [Fact]
    public async Task RecentCustomerReply_DoesNotHideAnOverdueTicket_ButAStaffReplyResolvesItsSla()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        var order = await Service(db, new(s.Customer.Id)).CreateOrderAsync(Request(s));
        var customer = new Actor(s.Customer.Id);
        var support = new SupportService(db, customer, Service(db, customer));
        var ticket = await support.CreateAsync(
            new()
            {
                OrderId = order.Id,
                Subject = "No answer",
                Message = "Please help",
            },
            default
        );
        (await db.SupportTickets.SingleAsync()).CreatedAt = DateTimeOffset.UtcNow.AddDays(-3);
        await db.SaveChangesAsync();
        await support.MessageAsync(ticket.Summary.Id, new("Following up", false), default);
        Assert.Single((await support.ListAsync(1, null, null, null, default, "overdue")).Content);
        Assert.Equal(1, (await new OperationsService(db).MetricsAsync(7, default)).OverdueTickets);
        var staff = new Actor(Guid.NewGuid(), "Manager");
        await new SupportService(db, staff, Service(db, staff)).MessageAsync(
            ticket.Summary.Id,
            new("We are investigating", false),
            default
        );
        Assert.Empty((await support.ListAsync(1, null, null, null, default, "overdue")).Content);
        Assert.Equal(0, (await new OperationsService(db).MetricsAsync(7, default)).OverdueTickets);
    }

    [Fact]
    public async Task RejectionSnapshot_ReportsRealResubmissionChanges_AndScopesSellers()
    {
        await using var db = TelegramTestData.Memory();
        var s = await SeedAsync(db);
        s.Product.Status = ProductStatus.PENDING_MODERATION;
        await db.SaveChangesAsync();
        await new ProductService(db).RejectAsync(s.Product.Id, "Fix the name");
        s.Product.Name = "Fixed name";
        s.Variant.SetStock(7);
        await db.SaveChangesAsync();
        var changes = await new ModerationComparisonService(db).CompareAsync(s.Product.Id, s.Seller.User.Id, default);
        Assert.NotNull(changes.CapturedAt);
        Assert.Contains(
            changes.Changes,
            c => c.Field == "Name" && c.Before == "Operations product" && c.After == "Fixed name"
        );
        Assert.Contains(changes.Changes, c => c.Field.EndsWith("Stock") && c.Before == "10" && c.After == "7");
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new ModerationComparisonService(db).CompareAsync(s.Product.Id, Guid.NewGuid(), default)
        );
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Seller", HttpStatusCode.Forbidden)]
    [InlineData("Moderator", HttpStatusCode.Forbidden)]
    [InlineData("Manager", HttpStatusCode.OK)]
    [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task OperationalHttpRoutes_EnforceStaffPolicies(string? role, HttpStatusCode expected)
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        foreach (
            var path in new[]
            {
                "api/v1/admin/promotions",
                "api/v1/admin/operations/customers",
                "api/v1/admin/operations/metrics",
            }
        )
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Fact]
    public async Task HttpCheckoutAndStaffWorkflow_WorkAcrossRequests_WithCurrentVersions()
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        Seed seed;
        using (var scope = server.Services.CreateScope())
            seed = await SeedAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Customer");
        client.DefaultRequestHeaders.Add("X-Test-UserId", seed.Customer.Id.ToString());
        using var create = await client.PostAsJsonAsync("api/v1/orders", Request(seed));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var order = (await create.Content.ReadFromJsonAsync<Contracts.Orders.OrderDto>())!;
        using var forbidden = await client.PostAsync($"api/v1/orders/{order.Id}/pay", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        var details = (await client.GetFromJsonAsync<OrderDetails>($"api/v1/admin/orders/{order.Id}"))!;
        using var changed = await client.PatchAsJsonAsync(
            $"api/v1/admin/orders/{order.Id}/status",
            new ChangeOrderStatusRequest("PAID", details.Version, "Verified bank transfer")
        );
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using var stale = await client.PatchAsJsonAsync(
            $"api/v1/admin/orders/{order.Id}/status",
            new ChangeOrderStatusRequest("CONFIRMED", details.Version, null)
        );
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var invalid = await client.PatchAsJsonAsync($"api/v1/admin/orders/{order.Id}/status", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
