using System.Net;
using System.Net.Http.Json;
using Application.Administration;
using Application.Common;
using Application.Reviews;
using Contracts.Admin.Reviews;
using Contracts.Admin.Settings;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests;

public sealed class ReviewRepliesTests
{
    [Fact]
    public async Task Templates_PersistBothLanguages_EnforceVersion_AndKeepDisabledOutOfModeration()
    {
        var actor = new OperationsTests.Actor(Guid.NewGuid(), "Administrator");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, actor);
        var service = new ReviewReplyTemplateService(db, actor);
        var created = await service.SaveAsync(null, new("Thanks", "Дякуємо, {customer}!", "Thank you, {customer}!"), default);
        await using var other = new ApplicationDbContext(options, actor);
        var reloaded = new ReviewReplyTemplateService(other, actor);
        Assert.Equal(created, Assert.Single(await reloaded.ListAsync(true, default)));
        var disabled = await reloaded.SaveAsync(created.Id, new("Thanks", "Дякуємо за {product}!", "Thank you for reviewing {product}!", false, created.Version), default);
        Assert.NotEqual(created.Version, disabled.Version);
        Assert.Empty(await new ReviewReplyTemplateService(other, new OperationsTests.Actor(Guid.NewGuid(), "Moderator")).ListAsync(false, default));
        Assert.Single(await reloaded.ListAsync(true, default));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveAsync(created.Id, new("Stale", "Привіт", "Hello", true, created.Version), default));
        await using var fresh = new ApplicationDbContext(options, actor);
        await Assert.ThrowsAsync<BusinessRuleException>(() => new ReviewReplyTemplateService(fresh, actor).SaveAsync(created.Id, new("Stale", "Привіт", "Hello", true, created.Version), default));
        Assert.NotEmpty(await db.AdminAuditLogs.Where(log => log.EntityType == "ReviewReplyTemplate").ToListAsync());
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Seller")]
    [InlineData("Manager")]
    [InlineData("Moderator")]
    public async Task Templates_CanOnlyBeManagedByAdministrators(string role)
    {
        await using var db = TelegramTestData.Memory();
        var service = new ReviewReplyTemplateService(db, new OperationsTests.Actor(Guid.NewGuid(), role));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(null, new("Name", "Текст", "Text"), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(true, default));
    }

    [Fact]
    public async Task Templates_RejectEmptyOversizedAndUnknownVariables()
    {
        await using var db = TelegramTestData.Memory();
        var service = new ReviewReplyTemplateService(db, new OperationsTests.Actor(Guid.NewGuid(), "Administrator"));
        foreach (var request in new[] { new SaveReviewReplyTemplateRequest(" ", "Текст", "Text"), new("Name", new string('x', 2001), "Text"), new("Name", "{password}", "Text"), new("Name", "Текст", " ") })
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(null, request, default));
        Assert.Empty(await db.ReviewReplyTemplates.ToListAsync());
    }

    [Fact]
    public async Task ReplyHttp_PublishesAtomically_UpdatesWithoutDoubleCounting_AndAppearsInPublicReviews()
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Moderator");
        client.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        var id = Guid.NewGuid();
        Guid productId;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seed = await OperationsTests.SeedAsync(db);
            productId = seed.Product.Id;
            db.Reviews.Add(new Review { Id = id, ProductId = productId, UserId = seed.Customer.Id, OrderItemId = Guid.NewGuid(), Rating = 5, Status = ReviewStatus.PENDING });
            await db.SaveChangesAsync();
        }
        var path = $"/api/v1/admin/reviews/{id}/moderate";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new ModerateReviewRequest("PUBLISHED", new string('x', 2001)))).StatusCode);
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(ReviewStatus.PENDING, (await db.Reviews.SingleAsync()).Status);
            Assert.Equal(0, (await db.Products.SingleAsync()).ReviewCount);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, new ModerateReviewRequest("PUBLISHED", "  Дякуємо за відгук!  "))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, new ModerateReviewRequest("PUBLISHED", "Оновлена відповідь"))).StatusCode);
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var review = await db.Reviews.SingleAsync();
            Assert.Equal("Оновлена відповідь", review.StaffReply);
            Assert.NotNull(review.StaffRepliedAt);
            Assert.NotNull(review.StaffRepliedBy);
            Assert.Equal(1, (await db.Products.SingleAsync()).ReviewCount);
            Assert.Equal(5, (await db.Products.SingleAsync()).AverageRating);
        }
        var publicReviews = await client.GetFromJsonAsync<PagedResult<ReviewDto>>($"/api/v1/reviews/products/{productId}");
        Assert.Equal("Оновлена відповідь", Assert.Single(publicReviews!.Items).StaffReply);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new ModerateReviewRequest("REJECTED", "Forbidden reply"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, new ModerateReviewRequest("REJECTED"))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<PagedResult<ReviewDto>>($"/api/v1/reviews/products/{productId}"))!.Items);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, new ModerateReviewRequest("PUBLISHED"))).StatusCode);
        Assert.Equal("Оновлена відповідь", Assert.Single((await client.GetFromJsonAsync<PagedResult<ReviewDto>>($"/api/v1/reviews/products/{productId}"))!.Items).StaffReply);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(path, new ModerateReviewRequest("PUBLISHED", ""))).StatusCode);
        Assert.Null(Assert.Single((await client.GetFromJsonAsync<PagedResult<ReviewDto>>($"/api/v1/reviews/products/{productId}"))!.Items).StaffReply);
    }

    [Fact]
    public async Task TemplateHttp_StaffCanReadActiveTemplates_ButOnlyAdministratorCanChangeThem()
    {
        await using var server = await AdminHttpTests.ServerAsync();
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        client.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        var path = "/api/v1/admin/settings/review-replies";
        var response = await client.PostAsJsonAsync(path, new SaveReviewReplyTemplateRequest("Fixture", "Дякуємо!", "Thank you!"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var template = (await response.Content.ReadFromJsonAsync<ReviewReplyTemplateDto>())!;
        client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", "Moderator");
        Assert.Equal(template.Id, Assert.Single((await client.GetFromJsonAsync<List<ReviewReplyTemplateDto>>("/api/v1/admin/reviews/reply-templates"))!).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path + "/" + template.Id, new SaveReviewReplyTemplateRequest("Fixture", "Текст", "Text", false, template.Version))).StatusCode);
    }
}
