using System.Text.Json;
using Contracts.Admin.Settings;
using Contracts.Common;
using Domain.Entities.Users;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Administration;

public sealed class StaffSettingsService(ApplicationDbContext db)
{
    public static readonly string[] EmployeeRoles = [Roles.Administrator, Roles.Manager, Roles.Moderator];

    public async Task<PagedResponse<StaffDto>> ListAsync(int page, string? search, string? role, CancellationToken ct)
    {
        if (page < 1 || page > 100000) throw new ArgumentException("Invalid page.");
        var query = db.Users.AsNoTracking().Include(user => user.Role)
            .Where(user => !user.IsDeleted && user.Role != null && EmployeeRoles.Contains(user.Role.Name));
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(user => user.Role!.Name == role);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(user => user.Email.ToLower().Contains(term) || user.FirstName.ToLower().Contains(term) || user.LastName.ToLower().Contains(term));
        }
        var total = await query.LongCountAsync(ct);
        var content = await query.OrderBy(user => user.Email).Skip((page - 1) * 20).Take(20)
            .Select(user => new StaffDto(user.Id, user.Email, user.FirstName, user.LastName, user.Role!.Name, user.Status.ToString(), user.CreatedAt)).ToListAsync(ct);
        return new(content, page, 20, total, (int)Math.Ceiling(total / 20d));
    }

    public async Task<StaffDto> CreateAsync(CreateStaffRequest request, CancellationToken ct)
    {
        ValidateRole(request.Role);
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(user => user.Email == email, ct))
            throw new UserDomainException("An account with this email already exists.");
        var roles = await db.Roles.ToListAsync(ct);
        var role = roles.Single(role => role.Name == request.Role);
        var user = User.Create(Guid.NewGuid(), email, null, request.FirstName, request.LastName);
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(request.Password));
        user.AssignRole(role.Id);
        user.VerifyEmail();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Map(user, role.Name);
    }

    public async Task<StaffDto?> UpdateAsync(Guid id, UpdateStaffAccessRequest request, CancellationToken ct)
    {
        ValidateRole(request.Role);
        var roles = await db.Roles.ToListAsync(ct);
        var user = await db.Users.Include(user => user.Role).SingleOrDefaultAsync(user => user.Id == id && !user.IsDeleted, ct);
        if (user is null || !EmployeeRoles.Contains(user.Role!.Name)) return null;
        var role = roles.Single(role => role.Name == request.Role);
        if (user.RoleId != role.Id) { user.AssignRole(role.Id); user.RevokeRefreshToken(); }
        if (request.Blocked && user.Status != UserStatus.Blocked) { user.Block(); user.RevokeRefreshToken(); }
        if (!request.Blocked && user.Status == UserStatus.Blocked) user.Unblock();
        await db.SaveChangesAsync(ct);
        return Map(user, role.Name);
    }

    public async Task<PagedResponse<AuditLogDto>> AuditAsync(int page, string? search, string? entityType, string? action, Guid? actorId,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        if (page < 1 || page > 100000) throw new ArgumentException("Invalid page.");
        var query = db.AdminAuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(log => log.ActorName.ToLower().Contains(term) || log.EntityName.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(log => log.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(log => log.Action == action);
        if (actorId.HasValue) query = query.Where(log => log.ActorId == actorId);
        if (from.HasValue) query = query.Where(log => log.OccurredAt >= from.Value);
        if (to.HasValue) query = query.Where(log => log.OccurredAt < to.Value);
        var total = await query.LongCountAsync(ct);
        var logs = await query.OrderByDescending(log => log.OccurredAt).ThenByDescending(log => log.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return new(logs.Select(log => new AuditLogDto(log.Id, log.OccurredAt, log.ActorId, log.ActorName, log.ActorRole, log.Action,
            log.EntityType, log.EntityId, log.EntityName, JsonSerializer.Deserialize<List<AuditChangeDto>>(log.ChangesJson) ?? [])).ToList(),
            page, 20, total, (int)Math.Ceiling(total / 20d));
    }

    private static void ValidateRole(string role)
    {
        if (!EmployeeRoles.Contains(role)) throw new ArgumentException("Choose Administrator, Manager or Moderator.");
    }
    private static StaffDto Map(User user, string role) => new(user.Id, user.Email, user.FirstName, user.LastName, role, user.Status.ToString(), user.CreatedAt);
}
