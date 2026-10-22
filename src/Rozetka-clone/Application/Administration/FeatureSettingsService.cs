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
        var row = await db.FeatureFlags.AsNoTracking().SingleOrDefaultAsync(x => x.Key == FeatureFlag.ChatDeletion, ct);
        return [View(row)];
    }

    public async Task<FeatureFlagDto> SetAsync(string key, SetFeatureFlagRequest request, CancellationToken ct)
    {
        RequireAdministrator();
        if (key != FeatureFlag.ChatDeletion) throw new ArgumentException("Невідома функція.");
        var row = await db.FeatureFlags.SingleOrDefaultAsync(x => x.Key == key, ct);
        if ((row?.Version ?? Guid.Empty) != request.ExpectedVersion)
            throw new BusinessRuleException("Налаштування змінено в іншому вікні. Оновіть сторінку.");
        if (row is null) { row = new() { Key = key }; db.FeatureFlags.Add(row); }
        row.Enabled = request.Enabled;
        row.Version = Guid.NewGuid(); row.UpdatedAt = DateTimeOffset.UtcNow; row.UpdatedBy = actor.UserId!.Value;
        await db.SaveChangesAsync(ct);
        return View(row);
    }

    private static FeatureFlagDto View(FeatureFlag? row) => new(FeatureFlag.ChatDeletion, row?.Enabled ?? false, row?.Version ?? Guid.Empty, row?.UpdatedAt);
}
