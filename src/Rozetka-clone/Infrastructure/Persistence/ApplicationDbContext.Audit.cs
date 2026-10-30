using System.Globalization;
using System.Text.Json;
using Contracts.Admin.Settings;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Infrastructure.Persistence;

public sealed partial class ApplicationDbContext
{
    private static readonly Dictionary<string, string[]> AuditFields = new()
    {
        ["User"] = ["Email", "FirstName", "LastName", "RoleId", "Status", "IsDeleted"],
        ["Product"] = ["Name", "Slug", "ShortDescription", "Description", "CategoryId", "BrandId", "Status", "WarrantyMonths", "CountryOfOrigin"],
        ["ProductVariant"] = ["Sku", "Barcode", "Price", "OldPrice", "StockQuantity", "IsActive"],
        ["ProductImage"] = ["ImageUrl", "AltText", "IsMain", "SortOrder"],
        ["ProductAttributeValue"] = ["AttributeId", "VariantId", "OptionId", "StringValue", "NumberValue", "BoolValue"],
        ["Category"] = ["Name", "Slug", "ParentId", "IsActive"],
        ["Brand"] = ["Name", "Slug", "IsActive"],
        ["Seller"] = ["CompanyName", "Status"],
        ["Store"] = ["Name", "Slug", "IsActive"],
        ["Review"] = ["Status", "StaffReply"]
        ,
        ["Order"] = ["Status", "TotalAmount"]
        ,
        ["Promotion"] = ["Name", "Status", "Type", "StartAt", "EndAt"]
        ,
        ["Coupon"] = ["Code", "Active", "UsageLimit", "UsageLimitPerUser"]
        ,
        ["PromotionAction"] = ["DiscountType", "DiscountValue", "MaximumDiscount"]
        ,
        ["PromotionRule"] = ["RuleType", "RuleValue"]
        ,
        ["SupportTicket"] = ["Status", "Kind", "Subject", "IsDeleted", "DeletedAt", "DeletedBy"]
        ,
        ["FeatureFlag"] = ["Key", "Enabled"]
        ,
        ["ReviewReplyTemplate"] = ["Name", "TextUk", "TextEn", "Enabled"]
        ,
        ["TelegramBinding"] = ["UserId", "TelegramUserId", "Username"]
        ,
        ["SellerCategoryGrant"] = ["SellerId", "CategoryId", "IncludeDescendants"]
        ,
        ["RozetkaCategoryMapping"] = ["ExternalKey", "CategoryId"]
    };

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var userChanges = ChangeTracker.Entries<User>().Where(entry => entry.State == EntityState.Deleted
            || entry.State == EntityState.Modified && new[] { "RoleId", "Status", "IsDeleted" }.Any(field => entry.Property(field).IsModified)).ToList();
        var ownsTransaction = userChanges.Count > 0 && Database.IsRelational() && Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
        var audit = new List<AdminAuditLog>();
        try
        {
            if (userChanges.Count > 0)
            {
                if (Database.IsNpgsql())
                    await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7473326101711)", cancellationToken);
                await ProtectAdministratorAccess(userChanges, cancellationToken);
            }
            audit = CreateAuditEntries();
            AdminAuditLogs.AddRange(audit);
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            foreach (var log in audit)
                Entry(log).State = EntityState.Detached;
            throw;
        }
    }

    private async Task ProtectAdministratorAccess(List<EntityEntry<User>> entries, CancellationToken ct)
    {
        if (entries.Any(entry => entry.Entity.Id == _actor?.UserId && (entry.State == EntityState.Deleted
            || entry.Entity.IsDeleted || entry.Entity.Status is UserStatus.Blocked or UserStatus.Deleted
            || entry.Property(user => user.RoleId).IsModified)))
            throw new UserDomainException("You cannot block your own account or change your own role.");

        var adminId = await Roles.AsNoTracking().Where(role => role.Name == Domain.Entities.Users.Roles.Administrator)
            .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(ct);
        if (adminId is null)
            return;
        var lost = entries.Count(entry => (Guid)entry.OriginalValues[nameof(User.RoleId)]! == adminId
            && (UserStatus)entry.OriginalValues[nameof(User.Status)]! == UserStatus.Active
            && !(bool)entry.OriginalValues[nameof(User.IsDeleted)]!
            && (entry.State == EntityState.Deleted || entry.Entity.RoleId != adminId || entry.Entity.IsDeleted || entry.Entity.Status != UserStatus.Active));
        if (lost == 0)
            return;
        var remaining = await Users.AsNoTracking().CountAsync(user => user.RoleId == adminId && user.Status == UserStatus.Active && !user.IsDeleted, ct);
        if (remaining - lost < 1)
            throw new UserDomainException("The last active administrator cannot be blocked or demoted.");
    }

    private List<AdminAuditLog> CreateAuditEntries()
    {
        if (_actor?.UserId is not { } actorId || _actor.Role is not ("Administrator" or "Manager" or "Moderator" or "Seller"))
            return [];
        var result = new List<AdminAuditLog>();
        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            var type = entry.Metadata.ClrType.Name;
            if (type == "Product" && AdminAuditLogs.Local.Any(log => log.EntityType == "Product" && log.EntityId == (Guid)entry.Property("Id").CurrentValue!
                && log.Action == "Moderation" && Entry(log).State == EntityState.Added))
                continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) || !AuditFields.TryGetValue(type, out var fields))
                continue;
            var changes = new List<AuditChangeDto>();
            foreach (var field in fields)
            {
                if (entry.Metadata.FindProperty(field) is null)
                    continue;
                var property = entry.Property(field);
                if (entry.State == EntityState.Modified && !property.IsModified)
                    continue;
                var before = entry.State == EntityState.Added ? null : Value(field, property.OriginalValue);
                var after = entry.State == EntityState.Deleted ? null : Value(field, property.CurrentValue);
                if (before != after)
                    changes.Add(new(field, before, after));
            }
            if (changes.Count == 0)
                continue;
            var labelField = new[] { "Name", "CompanyName", "Email", "Sku", "AltText", "Key" }.FirstOrDefault(field => entry.Metadata.FindProperty(field) is not null);
            var label = labelField is null ? type : Convert.ToString(entry.Property(labelField).CurrentValue, CultureInfo.InvariantCulture) ?? type;
            if (type == "ProductVariant")
                label += " · " + entry.Property("Sku").CurrentValue;
            result.Add(new AdminAuditLog
            {
                ActorId = actorId,
                ActorName = _actor.Name[..Math.Min(320, _actor.Name.Length)],
                ActorRole = _actor.Role,
                Action = type == "SupportTicket" && entry.State == EntityState.Modified && entry.Property("IsDeleted").IsModified
                    ? (bool)entry.Property("IsDeleted").CurrentValue! ? "Delete" : "Restore"
                    : entry.State switch { EntityState.Added => "Create", EntityState.Deleted => "Delete", _ => "Update" },
                EntityType = type,
                EntityId = (Guid)entry.Property("Id").CurrentValue!,
                EntityName = label[..Math.Min(500, label.Length)],
                ChangesJson = JsonSerializer.Serialize(changes)
            });
        }
        return result;
    }

    private string? Value(string field, object? value) => field == "RoleId" && value is Guid roleId
        ? Roles.Local.FirstOrDefault(role => role.Id == roleId)?.Name ?? roleId.ToString()
        : value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
}
