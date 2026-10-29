using Application.Abstractions;
using Application.Common;
using Contracts.Admin.Settings;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed class FeatureSettingsService(IApplicationDbContext db, ICurrentActor actor)
{
    private void RequireAdministrator()
    {
        if (actor.UserId is null || actor.Role != "Administrator") throw new UnauthorizedAccessException();
    }

    public async Task<IReadOnlyList<FeatureFlagDto>> ListAsync(CancellationToken ct)
    {
        RequireAdministrator();
        var keys = new[] { FeatureFlag.ChatDeletion, FeatureFlag.SmsSending };
        var rows = await db.FeatureFlags.AsNoTracking().Where(x => keys.Contains(x.Key)).ToListAsync(ct);
        return keys.Select(key => View(key, rows.SingleOrDefault(x => x.Key == key))).ToList();
    }

    public async Task<FeatureFlagDto> SetAsync(string key, SetFeatureFlagRequest request, CancellationToken ct)
    {
        RequireAdministrator();
        if (key is not (FeatureFlag.ChatDeletion or FeatureFlag.SmsSending)) throw new ArgumentException("Невідома функція.");
        var row = await db.FeatureFlags.SingleOrDefaultAsync(x => x.Key == key, ct);
        if ((row?.Version ?? Guid.Empty) != request.ExpectedVersion)
            throw new BusinessRuleException("Налаштування змінено в іншому вікні. Оновіть сторінку.");
        if (row is null) { row = new() { Key = key }; db.FeatureFlags.Add(row); }
        row.Enabled = request.Enabled;
        row.Version = Guid.NewGuid(); row.UpdatedAt = DateTimeOffset.UtcNow; row.UpdatedBy = actor.UserId!.Value;
        await db.SaveChangesAsync(ct);
        return View(key, row);
    }

    private static FeatureFlagDto View(string key, FeatureFlag? row) => new(key, row?.Enabled ?? false, row?.Version ?? Guid.Empty, row?.UpdatedAt);
}
