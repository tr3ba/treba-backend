using System.Net;
using System.Net.Http.Json;
using Application.Administration;
using Application.Common;
using Application.Marketplace;
using Contracts.Admin.Operations;
using Contracts.Admin.Settings;
using Domain.Entities;
using Domain.Entities.Support;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebApi.Tests;

public sealed class FeatureChatDeletionTests
{
    internal static SupportService Support(ApplicationDbContext db, OperationsTests.Actor actor) =>
        new(db, actor, OperationsTests.Service(db, actor));

    internal static Task<TicketDetails> Chat(SupportService customer) =>
        customer.CreateAsync(
            new()
            {
                Kind = "CHAT",
                Subject = "Support fixture",
                Message = "Help please",
            },
            default
        );

    [Fact]
    public async Task Feature_DefaultsOff_PersistsAcrossContexts_RejectsStaleAndUnknownFlags_AndAuditsChanges()
    {
        var actor = new OperationsTests.Actor(Guid.NewGuid(), "Administrator");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options, actor);
        var settings = new FeatureSettingsService(db, actor);
        var defaults = await settings.ListAsync(default);
        Assert.Equal(2, defaults.Count);
        Assert.All(defaults, flag => Assert.False(flag.Enabled));
        var initial = Assert.Single(defaults, flag => flag.Key == FeatureFlag.ChatDeletion);
        Assert.False(initial.Enabled);
        Assert.Equal(Guid.Empty, initial.Version);
        var enabled = await settings.SetAsync(FeatureFlag.ChatDeletion, new(true, initial.Version), default);
        Assert.True(enabled.Enabled);
        Assert.NotEqual(Guid.Empty, enabled.Version);
        await using var reloaded = new ApplicationDbContext(options, actor);
        Assert.True(Assert.Single(await new FeatureSettingsService(reloaded, actor).ListAsync(default), flag => flag.Key == FeatureFlag.ChatDeletion).Enabled);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            settings.SetAsync(FeatureFlag.ChatDeletion, new(false, initial.Version), default)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            settings.SetAsync("authentication.disable-mfa", new(true, Guid.Empty), default)
        );
        var disabled = await settings.SetAsync(FeatureFlag.ChatDeletion, new(false, enabled.Version), default);
        Assert.False(disabled.Enabled);
        Assert.Equal(2, await db.AdminAuditLogs.CountAsync(x => x.EntityType == "FeatureFlag"));
        Assert.Contains(
            "Enabled",
            (await db.AdminAuditLogs.OrderByDescending(x => x.OccurredAt).FirstAsync()).ChangesJson
        );
    }

    [Fact]
    public async Task Delete_HidesEveryCustomerReadAndNotification_PreservesMessages_AndRestoresWithFeatureOff()
    {
        var managerActor = new OperationsTests.Actor(Guid.NewGuid(), "Manager");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options, managerActor);
        var seed = await OperationsTests.SeedAsync(db);
        var customer = Support(db, new(seed.Customer.Id));
        var manager = Support(db, managerActor);
        var chat = await Chat(customer);
        Assert.False((await manager.CapabilitiesAsync(default)).CanDelete);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            manager.DeleteAsync(chat.Summary.Id, new(chat.Summary.Version), default)
        );
        db.FeatureFlags.Add(new() { Key = FeatureFlag.ChatDeletion, Enabled = true });
        await db.SaveChangesAsync();
        await manager.MessageAsync(chat.Summary.Id, new("Private note", true), default);
        await manager.MessageAsync(chat.Summary.Id, new("Public reply"), default);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            manager.DeleteAsync(chat.Summary.Id, new(chat.Summary.Version), default)
        );
        chat = (await manager.DetailsAsync(chat.Summary.Id, default))!;
        var count = await db.SupportMessages.CountAsync();
        Assert.NotEmpty(
            (await new NotificationService(db, new OperationsTests.Actor(seed.Customer.Id)).FeedAsync(default)).Items
        );
        await manager.DeleteAsync(chat.Summary.Id, new(chat.Summary.Version), default);
        Assert.Empty(await db.SupportTickets.ToListAsync());
        Assert.Equal(count, await db.SupportMessages.CountAsync());
        Assert.Empty((await customer.ListAsync(1, null, null, null, default)).Content);
        Assert.Null(await customer.DetailsAsync(chat.Summary.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => customer.MessagesAsync(chat.Summary.Id, null, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            customer.MessageAsync(chat.Summary.Id, new("After deletion"), default)
        );
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            customer.MarkReadAsync(chat.Summary.Id, new(chat.Messages[0].Id), default)
        );
        Assert.Empty((await new OperationsService(db).CustomerAsync(seed.Customer.Id, default))!.Tickets);
        Assert.Empty(
            (await new NotificationService(db, new OperationsTests.Actor(seed.Customer.Id)).FeedAsync(default)).Items
        );
        var trash = Assert.Single((await manager.DeletedAsync(1, null, default)).Content);
        Assert.NotNull(trash.DeletedAt);
        Assert.Equal(managerActor.UserId, (await db.SupportTickets.IgnoreQueryFilters().SingleAsync()).DeletedBy);
        var flag = await db.FeatureFlags.SingleAsync();
        flag.Enabled = false;
        await db.SaveChangesAsync();
        Assert.False((await manager.CapabilitiesAsync(default)).CanDelete);
        Assert.True((await manager.CapabilitiesAsync(default)).CanRestore);
        await manager.RestoreAsync(trash.Id, new(trash.Version), default);
        var restored = (await customer.DetailsAsync(trash.Id, default))!;
        Assert.Equal("IN_PROGRESS", restored.Summary.Status);
        Assert.Contains(restored.Messages, x => x.Message == "Public reply");
        Assert.DoesNotContain(restored.Messages, x => x.Internal);
        Assert.Equal(count, await db.SupportMessages.CountAsync());
        Assert.Empty((await manager.DeletedAsync(1, null, default)).Content);
        Assert.Contains(await db.AdminAuditLogs.ToListAsync(), x => x.EntityId == trash.Id && x.Action == "Delete");
        Assert.Contains(await db.AdminAuditLogs.ToListAsync(), x => x.EntityId == trash.Id && x.Action == "Restore");
    }

    [Fact]
    public async Task Restore_CannotOverwriteNewActiveChat_AndDeletionDoesNotGrantAccessToProductQuestionsOrActiveReturns()
    {
        await using var db = TelegramTestData.Memory();
        var seed = await OperationsTests.SeedAsync(db);
        db.FeatureFlags.Add(new() { Key = FeatureFlag.ChatDeletion, Enabled = true });
        await db.SaveChangesAsync();
        var manager = Support(db, new(Guid.NewGuid(), "Manager"));
        var customer = Support(db, new(seed.Customer.Id));
        var old = await Chat(customer);
        await manager.DeleteAsync(old.Summary.Id, new(old.Summary.Version), default);
        var replacement = await Chat(customer);
        Assert.NotEqual(old.Summary.Id, replacement.Summary.Id);
        var trash = Assert.Single((await manager.DeletedAsync(1, null, default)).Content);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            manager.RestoreAsync(trash.Id, new(trash.Version), default)
        );
        await manager.ChangeStatusAsync(replacement.Summary.Id, new("CLOSED", replacement.Summary.Version), default);
        await manager.RestoreAsync(trash.Id, new(trash.Version), default);
        var question = await customer.CreateAsync(
            new()
            {
                Kind = "QUESTION",
                ProductId = seed.Product.Id,
                Subject = "Private product question",
                Message = "Question",
            },
            default
        );
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            manager.DeleteAsync(question.Summary.Id, new(question.Summary.Version), default)
        );
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Support(db, new(seed.Seller.User.Id, "Seller"))
                .DeleteAsync(question.Summary.Id, new(question.Summary.Version), default)
        );
        var activeReturn = new SupportTicket
        {
            CustomerId = seed.Customer.Id,
            Kind = "RETURN",
            Status = "APPROVED",
            Subject = "Return",
        };
        db.SupportTickets.Add(activeReturn);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            manager.DeleteAsync(activeReturn.Id, new(activeReturn.Version), default)
        );
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Seller", HttpStatusCode.Forbidden)]
    [InlineData("Moderator", HttpStatusCode.Forbidden)]
    [InlineData("Manager", HttpStatusCode.Forbidden)]
    [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task FeatureHttpRoutes_OnlyAdministratorsCanReadOrToggle(string? role, HttpStatusCode expected)
    {
        await using var app = await AdminHttpTests.ServerAsync();
        using var client = app.GetTestClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        }
        Assert.Equal(expected, (await client.GetAsync("/api/v1/admin/settings/features")).StatusCode);
        Assert.Equal(
            expected,
            (
                await client.PutAsJsonAsync(
                    "/api/v1/admin/settings/features/" + FeatureFlag.ChatDeletion,
                    new SetFeatureFlagRequest(true, Guid.Empty)
                )
            ).StatusCode
        );
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Customer", HttpStatusCode.Forbidden)]
    [InlineData("Seller", HttpStatusCode.Forbidden)]
    [InlineData("Moderator", HttpStatusCode.Forbidden)]
    [InlineData("Manager", HttpStatusCode.OK)]
    [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task TrashHttpRoutes_RequireOperationsStaff_AndCheckTheFeatureOnDelete(
        string? role,
        HttpStatusCode expected
    )
    {
        await using var app = await AdminHttpTests.ServerAsync();
        using var client = app.GetTestClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        }
        var id = Guid.NewGuid();
        Assert.Equal(expected, (await client.GetAsync("/api/v1/support/deleted")).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/support/" + id)
        {
            Content = JsonContent.Create(new TicketDeletionRequest(Guid.Empty)),
        };
        Assert.Equal(
            expected == HttpStatusCode.OK ? HttpStatusCode.Conflict : expected,
            (await client.SendAsync(request)).StatusCode
        );
        Assert.Equal(
            expected == HttpStatusCode.OK ? HttpStatusCode.NotFound : expected,
            (
                await client.PostAsJsonAsync($"/api/v1/support/{id}/restore", new TicketDeletionRequest(Guid.Empty))
            ).StatusCode
        );
    }

    [Fact]
    public async Task HttpWorkflow_AdminToggles_ManagerDeletesAndRestores_CustomerAccessTracksTheTrashState()
    {
        await using var app = await AdminHttpTests.ServerAsync();
        OperationsTests.Seed seed;
        using (var scope = app.Services.CreateScope())
            seed = await OperationsTests.SeedAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        using var customer = app.GetTestClient();
        customer.DefaultRequestHeaders.Add("X-Test-Role", "Customer");
        customer.DefaultRequestHeaders.Add("X-Test-UserId", seed.Customer.Id.ToString());
        using var manager = app.GetTestClient();
        manager.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        manager.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        using var administrator = app.GetTestClient();
        administrator.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        administrator.DefaultRequestHeaders.Add("X-Test-UserId", Guid.NewGuid().ToString());
        var feature = await administrator.PutAsJsonAsync(
            "/api/v1/admin/settings/features/" + FeatureFlag.ChatDeletion,
            new SetFeatureFlagRequest(true, Guid.Empty)
        );
        Assert.Equal(HttpStatusCode.OK, feature.StatusCode);
        var created = await customer.PostAsJsonAsync(
            "/api/v1/support",
            new CreateTicketRequest
            {
                Kind = "CHAT",
                Subject = "HTTP fixture",
                Message = "Hello",
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var chat = (await created.Content.ReadFromJsonAsync<TicketDetails>())!;
        using var deletion = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/support/" + chat.Summary.Id)
        {
            Content = JsonContent.Create(new TicketDeletionRequest(chat.Summary.Version)),
        };
        Assert.Equal(HttpStatusCode.NoContent, (await manager.SendAsync(deletion)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await customer.GetAsync("/api/v1/support/" + chat.Summary.Id)).StatusCode
        );
        var trash = await manager.GetFromJsonAsync<Contracts.Common.PagedResponse<TicketRow>>(
            "/api/v1/support/deleted"
        );
        var deleted = Assert.Single(trash!.Content);
        Assert.NotEqual(chat.Summary.Version, deleted.Version);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await manager.PostAsJsonAsync(
                    $"/api/v1/support/{deleted.Id}/restore",
                    new TicketDeletionRequest(deleted.Version)
                )
            ).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/v1/support/" + chat.Summary.Id)).StatusCode);
    }
}
