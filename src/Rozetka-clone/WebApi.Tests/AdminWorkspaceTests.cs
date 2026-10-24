using AdminPanel.Auth;
using Application.Administration;
using Application.Common;
using Application.Reviews;
using Application.Sellers;
using Domain.Entities;
using Domain.Entities.Product;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WebApi.Controllers;

namespace WebApi.Tests;

public sealed class AdminWorkspaceTests
{
    private static ApplicationDbContext Database() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Product Product, User Author)> SeedAsync(ApplicationDbContext db)
    {
        var author = User.Create(Guid.NewGuid(), "shopper@example.com", null, "Anna", "Buyer");
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Coffee grinder",
            Slug = "coffee-grinder",
            UpdatedAt = DateTime.UtcNow,
        };
        db.Users.Add(author);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (product, author);
    }

    private static Review Review(Product product, User author, ReviewStatus status, int rating = 4) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UserId = author.Id,
            OrderItemId = Guid.NewGuid(),
            Rating = rating,
            Title = "Useful product",
            Comment = "Works well",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task ReviewQueue_FiltersByAuthorAndStatus_AndExcludesDeleted()
    {
        await using var db = Database();
        var (product, author) = await SeedAsync(db);
        db.Reviews.AddRange(
            Review(product, author, ReviewStatus.PENDING),
            Review(product, author, ReviewStatus.PUBLISHED),
            Review(product, author, ReviewStatus.DELETED)
        );
        await db.SaveChangesAsync();
        var service = new AdminWorkspaceService(db);
        var pending = await service.ReviewsAsync(1, 12, "pending", " ANNA ");
        Assert.Single(pending.Content);
        Assert.Equal("Coffee grinder", pending.Content[0].ProductName);
        Assert.Equal("Anna Buyer", pending.Content[0].AuthorName);
        Assert.Equal("PENDING", pending.Content[0].Status);
        Assert.Equal(2, (await service.ReviewsAsync(1, 12, null, null)).TotalElements);
        Assert.Equal(1, await service.PendingReviewsAsync());
    }

    [Fact]
    public async Task ReviewQueue_PaginatesWithStableOrdering()
    {
        await using var db = Database();
        var (product, author) = await SeedAsync(db);
        var time = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            var review = Review(product, author, ReviewStatus.PENDING);
            review.CreatedAt = time;
            db.Reviews.Add(review);
        }
        await db.SaveChangesAsync();
        var service = new AdminWorkspaceService(db);
        var first = await service.ReviewsAsync(1, 2, "PENDING", null);
        var second = await service.ReviewsAsync(2, 2, "PENDING", null);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(5, first.TotalElements);
        Assert.Empty(first.Content.Select(x => x.Id).Intersect(second.Content.Select(x => x.Id)));
        Assert.Single((await service.ReviewsAsync(3, 2, "PENDING", null)).Content);
    }

    [Theory]
    [InlineData(0, 12, "PENDING")]
    [InlineData(1, 101, "PENDING")]
    [InlineData(int.MaxValue, 100, "PENDING")]
    [InlineData(1, 12, "DELETED")]
    [InlineData(1, 12, "99")]
    public async Task ReviewQueue_RejectsInvalidFilters(int page, int size, string status)
    {
        await using var db = Database();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AdminWorkspaceService(db).ReviewsAsync(page, size, status, null)
        );
    }

    [Fact]
    public async Task Moderation_PublishesAndRejects_UpdatingOnlyPublishedRating()
    {
        await using var db = Database();
        var (product, author) = await SeedAsync(db);
        var pending = Review(product, author, ReviewStatus.PENDING, 5);
        db.Reviews.AddRange(
            pending,
            Review(product, author, ReviewStatus.PUBLISHED, 3),
            Review(product, author, ReviewStatus.REJECTED, 1)
        );
        await db.SaveChangesAsync();
        var service = new ReviewService(db);
        await service.ModerateReviewAsync(pending.Id, ReviewStatus.PUBLISHED);
        Assert.Equal(2, product.ReviewCount);
        Assert.Equal(4, product.AverageRating);
        await service.ModerateReviewAsync(pending.Id, ReviewStatus.REJECTED);
        Assert.Equal(1, product.ReviewCount);
        Assert.Equal(3, product.AverageRating);
        db.ChangeTracker.Clear();
        Assert.Equal(ReviewStatus.REJECTED, (await db.Reviews.SingleAsync(x => x.Id == pending.Id)).Status);
        Assert.Equal(3, (await db.Products.SingleAsync()).AverageRating);
    }

    [Fact]
    public async Task Moderation_RejectingLastPublishedReview_ResetsRating()
    {
        await using var db = Database();
        var (product, author) = await SeedAsync(db);
        var review = Review(product, author, ReviewStatus.PUBLISHED, 5);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        await new ReviewService(db).ModerateReviewAsync(review.Id, ReviewStatus.REJECTED);
        Assert.Equal(0, product.ReviewCount);
        Assert.Equal(0, product.AverageRating);
    }

    [Fact]
    public async Task Moderation_RejectsDeletedAndInvalidStates()
    {
        await using var db = Database();
        var (product, author) = await SeedAsync(db);
        var review = Review(product, author, ReviewStatus.DELETED);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        var service = new ReviewService(db);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ModerateReviewAsync(review.Id, ReviewStatus.PUBLISHED)
        );
        await Assert.ThrowsAsync<ArgumentException>(() => service.ModerateReviewAsync(review.Id, ReviewStatus.PENDING));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ModerateReviewAsync(Guid.NewGuid(), ReviewStatus.PUBLISHED)
        );
        Assert.Equal(ReviewStatus.DELETED, review.Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Dashboard_RestrictsCountsAndActivityToGrantedAreas(bool sellers, bool users)
    {
        await using var db = Database();
        var (product, author) = await SeedAsync(db);
        db.Reviews.Add(Review(product, author, ReviewStatus.PENDING));
        db.Sellers.Add(
            new Seller
            {
                Id = Guid.NewGuid(),
                UserId = author.Id,
                CompanyName = "Store",
                Status = SellerStatus.PENDING,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
        var dashboard = await new AdminWorkspaceService(db).DashboardAsync(7, sellers, users);
        Assert.Equal(1, dashboard.PendingReviews);
        Assert.Equal(sellers ? 1 : (int?)null, dashboard.PendingSellers);
        Assert.Equal(users ? 1 : (int?)null, dashboard.TotalUsers);
        Assert.Equal(users ? 1 : (int?)null, dashboard.NewUsers);
        Assert.Equal(sellers, dashboard.RecentActivity.Any(x => x.Kind == "seller"));
        Assert.Equal(users, dashboard.RecentActivity.Any(x => x.Kind == "user"));
        Assert.Equal(
            dashboard.RecentActivity.OrderByDescending(x => x.OccurredAt).Select(x => x.EntityId),
            dashboard.RecentActivity.Select(x => x.EntityId)
        );
    }

    [Fact]
    public async Task Dashboard_ExcludesOldActivity_AndRejectsInvalidPeriod()
    {
        await using var db = Database();
        var (product, _) = await SeedAsync(db);
        product.UpdatedAt = DateTime.UtcNow.AddDays(-60);
        await db.SaveChangesAsync();
        var service = new AdminWorkspaceService(db);
        Assert.Empty((await service.DashboardAsync(7, false, false)).RecentActivity);
        Assert.Contains(
            (await service.DashboardAsync(90, false, false)).RecentActivity,
            item => item.Kind == "product"
        );
        await Assert.ThrowsAsync<ArgumentException>(() => service.DashboardAsync(0, true, true));
    }

    [Fact]
    public async Task SellerDecisions_ArePersisted_AndMissingSellerReturnsNull()
    {
        await using var db = Database();
        var seller = new Seller
        {
            Id = Guid.NewGuid(),
            CompanyName = "Store",
            Status = SellerStatus.PENDING,
        };
        db.Sellers.Add(seller);
        await db.SaveChangesAsync();
        var service = new SellerService(db);
        Assert.Equal(SellerStatus.ACTIVE, (await service.ApproveAsync(seller.Id))!.Status);
        Assert.Equal(SellerStatus.SUSPENDED, (await service.SuspendAsync(seller.Id))!.Status);
        db.ChangeTracker.Clear();
        Assert.Equal(SellerStatus.SUSPENDED, (await db.Sellers.SingleAsync()).Status);
        Assert.Null(await service.ApproveAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("Manager", true)]
    [InlineData("Moderator", true)]
    [InlineData("Seller", false)]
    [InlineData("Customer", false)]
    public void ReviewPageAndApi_GrantSameRoles(string role, bool allowed)
    {
        Assert.Equal(allowed, AdminAccess.CanAccess(role, "/reviews"));
        foreach (var type in new[] { typeof(AdminReviewsController), typeof(AdminDashboardController) })
        {
            var policy = Assert.Single(
                type.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>()
            );
            Assert.Equal(allowed, policy.Roles!.Split(',').Contains(role));
        }
    }
}
