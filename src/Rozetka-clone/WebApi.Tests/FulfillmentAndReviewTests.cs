using System.Net;
using System.Net.Http.Json;
using Application.Common;
using Application.Marketplace;
using Application.Reviews;
using Contracts.Admin.Operations;
using Contracts.Marketplace;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests;

public sealed class FulfillmentAndReviewTests
{
    private static CreateReviewRequest Review(OperationsTests.Seed seed, Guid itemId) =>
        new() { ProductId = seed.Product.Id, OrderItemId = itemId, Rating = 5, Title = "Покупка", Comment = "Товар отримано, все добре." };

    [Fact]
    public async Task PaidOrder_CanBeFulfilled_ReceiptNotifiesOnlyBuyer_AndUnlocksOneModeratedReview()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await OperationsTests.SeedAsync(db);
        db.CustomerNotificationSettings.Add(new() { UserId = seed.Customer.Id, OrdersEmail = true, OrdersTelegram = true });
        await db.SaveChangesAsync();
        var buyer = new OperationsTests.Actor(seed.Customer.Id);
        var manager = new OperationsTests.Actor(seed.Seller.User.Id, "Manager");
        var clock = new TelegramTestData.Clock();
        var delivery = DeliveryAndNotificationTests.Delivery(db, buyer, clock);
        var orders = DeliveryAndNotificationTests.Orders(db, buyer, delivery);
        var order = await orders.CreateOrderAsync(OperationsTests.Request(seed));
        var staff = DeliveryAndNotificationTests.Orders(db, manager, DeliveryAndNotificationTests.Delivery(db, manager, clock));
        var account = new CustomerAccountService(db, buyer, delivery);
        var reviews = new ReviewService(db);
        var item = Assert.Single((await account.OrderAsync(order.Id, default))!.Items);
        Assert.False(item.CanReview);
        Assert.Empty((await reviews.GetProductsAwaitingReviewAsync(buyer.UserId!.Value, 1)).Items);

        foreach (var status in new[] { "PAID", "CONFIRMED", "PROCESSING", "READY_FOR_SHIPMENT", "SHIPPED", "DELIVERED" })
        {
            var details = (await staff.DetailsAsync(order.Id, default))!;
            Assert.Contains(status, details.AllowedStatuses);
            await staff.ChangeStatusAsync(order.Id, new(status, details.Version, "Private staff note"), default);
            await Assert.ThrowsAsync<BusinessRuleException>(() => staff.ChangeStatusAsync(order.Id, new(status, details.Version, "Repeat"), default));
            if (status != "DELIVERED")
                await Assert.ThrowsAsync<BusinessRuleException>(() => reviews.CreateReviewAsync(seed.Customer.Id, Review(seed, item.Id)));
        }

        var received = (await account.OrderAsync(order.Id, default))!;
        Assert.True(Assert.Single(received.Items).CanReview);
        Assert.Equal("DELIVERED", received.Delivery!.Status);
        Assert.Equal("TRB-", received.Delivery.Reference![..4]);
        var feed = await new NotificationService(db, buyer).FeedAsync(default);
        var receipt = Assert.Single(feed.Items, n => n.Title == "Замовлення отримано");
        Assert.Contains("За бажанням", receipt.Text);
        Assert.Equal("/account/orders/" + order.Id, receipt.Path);
        Assert.DoesNotContain("Private", receipt.Text);
        Assert.DoesNotContain((await new NotificationService(db, manager).FeedAsync(default)).Items, n => n.Title == "Замовлення отримано");
        var external = await db.CustomerNotifications.Where(n => n.Title == "Замовлення отримано").ToListAsync();
        Assert.Equal(2, external.Count);
        Assert.All(external, n => { Assert.Equal(seed.Customer.Id, n.UserId); Assert.Contains("За бажанням", n.Text); Assert.DoesNotContain("Private", n.Text); });

        var eligible = Assert.Single((await reviews.GetProductsAwaitingReviewAsync(seed.Customer.Id, 1)).Items);
        Assert.Equal(item.Id, eligible.OrderItemId);
        Assert.Empty((await reviews.GetProductsAwaitingReviewAsync(seed.Seller.User.Id, 1)).Items);
        await Assert.ThrowsAsync<BusinessRuleException>(() => reviews.CreateReviewAsync(seed.Seller.User.Id, Review(seed, item.Id)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reviews.CreateReviewAsync(seed.Customer.Id,
            new() { ProductId = Guid.NewGuid(), OrderItemId = item.Id, Rating = 5, Comment = "Wrong product" }));
        var review = await reviews.CreateReviewAsync(seed.Customer.Id, Review(seed, item.Id));
        Assert.True(review.VerifiedPurchase);
        Assert.Equal(ReviewStatus.PENDING, review.Status);
        Assert.Empty((await reviews.GetProductReviewsAsync(seed.Product.Id, 1, 20)).Items);
        Assert.Empty((await reviews.GetProductsAwaitingReviewAsync(seed.Customer.Id, 1)).Items);
        var reviewedItem = Assert.Single((await account.OrderAsync(order.Id, default))!.Items);
        Assert.False(reviewedItem.CanReview);
        Assert.Equal(review.Id, reviewedItem.ReviewId);
        Assert.Equal(review.Id, Assert.Single((await reviews.GetMyReviewsAsync(seed.Customer.Id, 1)).Items).Id);
        Assert.Empty((await reviews.GetMyReviewsAsync(seed.Seller.User.Id, 1)).Items);
        await Assert.ThrowsAsync<BusinessRuleException>(() => reviews.CreateReviewAsync(seed.Customer.Id, Review(seed, item.Id)));
        await reviews.ModerateReviewAsync(review.Id, ReviewStatus.PUBLISHED);
        Assert.Equal(review.Id, Assert.Single((await reviews.GetProductReviewsAsync(seed.Product.Id, 1, 20)).Items).Id);
        var delivered = (await staff.DetailsAsync(order.Id, default))!;
        await staff.ChangeStatusAsync(order.Id, new("COMPLETED", delivered.Version, null), default);
        Assert.NotNull((await db.Orders.SingleAsync()).CompletedAt);
        Assert.Equal(2, await db.CustomerNotifications.CountAsync(n => n.Title == "Замовлення отримано"));
    }

    [Theory]
    [InlineData(OrderStatus.AWAITING_PAYMENT, PaymentStatus.PENDING)]
    [InlineData(OrderStatus.PAID, PaymentStatus.SUCCEEDED)]
    [InlineData(OrderStatus.CONFIRMED, PaymentStatus.SUCCEEDED)]
    [InlineData(OrderStatus.PROCESSING, PaymentStatus.SUCCEEDED)]
    [InlineData(OrderStatus.READY_FOR_SHIPMENT, PaymentStatus.SUCCEEDED)]
    [InlineData(OrderStatus.SHIPPED, PaymentStatus.SUCCEEDED)]
    [InlineData(OrderStatus.CANCELLED, PaymentStatus.SUCCEEDED)]
    [InlineData(OrderStatus.RETURNED, PaymentStatus.SUCCEEDED)]
    [InlineData(OrderStatus.RETURN_REQUESTED, PaymentStatus.SUCCEEDED)]
    [InlineData(OrderStatus.DELIVERED, PaymentStatus.PENDING)]
    [InlineData(OrderStatus.COMPLETED, PaymentStatus.REFUNDED)]
    public async Task Reviews_RejectUnreceivedCancelledReturnedAndUnpaidOrders(OrderStatus status, PaymentStatus payment)
    {
        await using var db = TelegramTestData.Memory();
        var seed = await OperationsTests.SeedAsync(db);
        var actor = new OperationsTests.Actor(seed.Customer.Id);
        var created = await OperationsTests.Service(db, actor).CreateOrderAsync(OperationsTests.Request(seed));
        var order = await db.Orders.SingleAsync(); order.Status = status; order.PaymentStatus = payment;
        await db.SaveChangesAsync();
        var item = Assert.Single((await new CustomerAccountService(db, actor).OrderAsync(created.Id, default))!.Items);
        Assert.False(item.CanReview);
        var reviews = new ReviewService(db);
        Assert.Empty((await reviews.GetProductsAwaitingReviewAsync(seed.Customer.Id, 1)).Items);
        await Assert.ThrowsAsync<BusinessRuleException>(() => reviews.CreateReviewAsync(seed.Customer.Id, Review(seed, item.Id)));
        Assert.Empty(db.Reviews);
    }

    [Fact]
    public async Task ReviewHttp_RequiresLoginAndReceipt_ValidatesContent_AndListsOnlyOwnReviews()
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/reviews/awaiting")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/reviews/mine")).StatusCode);
        OperationsTests.Seed seed;
        Guid orderId, itemId;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seed = await OperationsTests.SeedAsync(db);
            orderId = (await OperationsTests.Service(db, new(seed.Customer.Id)).CreateOrderAsync(OperationsTests.Request(seed))).Id;
            itemId = (await db.OrderItems.SingleAsync()).Id;
        }
        client.DefaultRequestHeaders.Add("X-Test-Role", "Customer");
        client.DefaultRequestHeaders.Add("X-Test-UserId", seed.Customer.Id.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/reviews/awaiting?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/reviews", Review(seed, itemId))).StatusCode);
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.SingleAsync(o => o.Id == orderId);
            order.Status = OrderStatus.COMPLETED; order.PaymentStatus = PaymentStatus.SUCCEEDED;
            await db.SaveChangesAsync();
        }
        Assert.Single((await client.GetFromJsonAsync<PagedResult<ReviewableProductDto>>("/api/v1/reviews/awaiting"))!.Items);
        foreach (var input in new[] {
            new { productId = seed.Product.Id, orderItemId = itemId, rating = 0, comment = "Valid comment" },
            new { productId = seed.Product.Id, orderItemId = itemId, rating = 5, comment = "   " },
            new { productId = seed.Product.Id, orderItemId = itemId, rating = 5, comment = new string('a', 10001) }
        }) Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/reviews", input)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/reviews", Review(seed, itemId))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/reviews", Review(seed, itemId))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<PagedResult<ReviewableProductDto>>("/api/v1/reviews/awaiting"))!.Items);
        Assert.Single((await client.GetFromJsonAsync<PagedResult<ReviewDto>>("/api/v1/reviews/mine"))!.Items);
        client.DefaultRequestHeaders.Remove("X-Test-UserId");
        client.DefaultRequestHeaders.Add("X-Test-UserId", seed.Seller.User.Id.ToString());
        Assert.Empty((await client.GetFromJsonAsync<PagedResult<ReviewDto>>("/api/v1/reviews/mine"))!.Items);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/reviews", Review(seed, itemId))).StatusCode);
    }
}
