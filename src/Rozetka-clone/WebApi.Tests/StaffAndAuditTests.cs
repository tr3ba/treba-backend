using Application.Abstractions;
using Contracts.Admin.Settings;
using Domain.Entities.Product;
using Domain.Entities.Users;
using Infrastructure.Administration;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Tests;

public sealed class StaffAndAuditTests
{
    private sealed class Actor : ICurrentActor
    {
        public Guid? UserId { get; set; }
        public string Name => "Test Administrator";
        public string Role => "Administrator";
    }

    private static async Task<(ApplicationDbContext Db, Actor Actor, User Admin, User Other)> Setup(int administrators = 2)
    {
        var actor = new Actor();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, actor);
        var adminRole = Role.Create("Administrator", "Administrator");
        var managerRole = Role.Create("Manager", "Manager");
        var moderatorRole = Role.Create("Moderator", "Moderator");
        var customerRole = Role.Create("Customer", "Customer");
        db.Roles.AddRange(adminRole, managerRole, moderatorRole, customerRole);
        var admin = User.Create(Guid.NewGuid(), "admin@example.test", null, "Admin", "One");
        admin.AssignRole(adminRole.Id); admin.VerifyEmail();
        var other = User.Create(Guid.NewGuid(), "other@example.test", null, "Other", "Staff");
        other.AssignRole(administrators == 2 ? adminRole.Id : managerRole.Id); other.VerifyEmail();
        db.Users.AddRange(admin, other);
        await db.SaveChangesAsync();
        actor.UserId = admin.Id;
        return (db, actor, admin, other);
    }

    [Fact]
    public async Task CreateStaff_ProducesUsableAccountAndAuditWithoutCredentials()
    {
        var (db, _, _, _) = await Setup(); await using var cleanup = db;
        var created = await new StaffSettingsService(db).CreateAsync(new CreateStaffRequest
        {
            Email = " NEW@EXAMPLE.TEST ", FirstName = "New", LastName = "Staff", Role = "Moderator", Password = "Private-password-123!"
        }, default);
        var user = await db.Users.SingleAsync(user => user.Id == created.Id);
        Assert.Equal("new@example.test", created.Email);
        Assert.Equal("Moderator", created.Role);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(BCrypt.Net.BCrypt.Verify("Private-password-123!", user.PasswordHash));
        var audit = await db.AdminAuditLogs.SingleAsync();
        Assert.Equal("Create", audit.Action);
        Assert.DoesNotContain("Private-password", audit.ChangesJson);
        Assert.DoesNotContain("PasswordHash", audit.ChangesJson);
        Assert.Contains("Moderator", audit.ChangesJson);
    }

    [Fact]
    public async Task UpdateAccess_RevokesRefreshTokenAndRecordsRoleNames()
    {
        var (db, _, _, other) = await Setup(); await using var cleanup = db;
        other.SetRefreshToken("old-session-secret", DateTimeOffset.UtcNow.AddDays(1)); await db.SaveChangesAsync();
        await new StaffSettingsService(db).UpdateAsync(other.Id, new("Moderator", true), default);
        Assert.Null(other.RefreshToken);
        Assert.Equal(UserStatus.Blocked, other.Status);
        var log = await db.AdminAuditLogs.SingleAsync();
        Assert.Contains("Administrator", log.ChangesJson);
        Assert.Contains("Moderator", log.ChangesJson);
        Assert.DoesNotContain("old-session-secret", log.ChangesJson);
    }

    [Fact]
    public async Task BlockingSelf_IsPreventedEvenThroughLegacyUserMutation()
    {
        var (db, _, admin, _) = await Setup(); await using var cleanup = db;
        admin.Block();
        await Assert.ThrowsAsync<UserDomainException>(() => db.SaveChangesAsync());
        Assert.Empty(await db.AdminAuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LastAdministrator_CannotBeBlockedOrDemoted(bool block)
    {
        var (db, actor, admin, other) = await Setup(1); await using var cleanup = db;
        actor.UserId = other.Id;
        if (block) admin.Block(); else admin.AssignRole((await db.Roles.SingleAsync(role => role.Name == "Manager")).Id);
        await Assert.ThrowsAsync<UserDomainException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PasswordAndSessionUpdates_AreNeverAudited()
    {
        var (db, _, _, other) = await Setup(); await using var cleanup = db;
        other.SetPasswordHash("sensitive-password-hash");
        other.SetRefreshToken("sensitive-session-token", DateTimeOffset.UtcNow.AddDays(1));
        await db.SaveChangesAsync();
        Assert.Empty(await db.AdminAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task PriceChange_KeepsBeforeAndAfterValues()
    {
        var (db, actor, _, _) = await Setup(); await using var cleanup = db;
        actor.UserId = null;
        var variant = new ProductVariant(Guid.NewGuid(), Guid.NewGuid(), "SKU-TEST", null, "Variant", 100, null, null, null, null, null, null, true);
        db.ProductVariants.Add(variant); await db.SaveChangesAsync();
        actor.UserId = Guid.NewGuid(); variant.Update("SKU-TEST", null, "Variant", 125, null, null, null, null, null, null, true); await db.SaveChangesAsync();
        var changes = (await new StaffSettingsService(db).AuditAsync(1, "SKU-TEST", "ProductVariant", "Update", null, null, null, default)).Content.Single().Changes;
        Assert.Contains(changes, change => change.Field == "Price" && change.Before == "100" && change.After == "125");
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Seller")]
    [InlineData("Unknown")]
    public async Task StaffEndpoint_CannotAssignUnrelatedRoles(string role)
    {
        var (db, _, _, _) = await Setup(); await using var cleanup = db;
        await Assert.ThrowsAsync<ArgumentException>(() => new StaffSettingsService(db).CreateAsync(new CreateStaffRequest { Role = role }, default));
    }

    [Fact]
    public async Task DuplicateEmail_DoesNotTakeOverExistingAccount()
    {
        var (db, _, admin, _) = await Setup(); await using var cleanup = db;
        await Assert.ThrowsAsync<UserDomainException>(() => new StaffSettingsService(db).CreateAsync(new CreateStaffRequest
        { Email = " ADMIN@EXAMPLE.TEST ", Role = "Manager", Password = "some-password-123!", FirstName = "Another", LastName = "Person" }, default));
        Assert.Equal("Administrator", (await db.Roles.SingleAsync(role => role.Id == admin.RoleId)).Name);
        Assert.Empty(await db.AdminAuditLogs.ToListAsync());
    }
}
