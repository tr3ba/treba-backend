using Application.Abstractions;
using Application.Common;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Catalog;

public sealed class SellerCategoryAccess(IApplicationDbContext db)
{
    public async Task<bool> IsAllowedAsync(Guid userId, Guid categoryId, CancellationToken ct)
        => (await AllowedIdsAsync(userId, ct)).Contains(categoryId);

    public async Task<IReadOnlyList<Guid>> AllowedIdsAsync(Guid userId, CancellationToken ct)
    {
        var sellerId = await db.Sellers.Where(s => s.UserId == userId && s.Status == SellerStatus.ACTIVE)
            .Select(s => (Guid?)s.Id).SingleOrDefaultAsync(ct);
        if (sellerId is null) return [];
        var categories = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId, c.IsActive }).ToListAsync(ct);
        var grants = await db.SellerCategoryGrants.AsNoTracking().Where(g => g.SellerId == sellerId).ToListAsync(ct);
        var byId = categories.ToDictionary(c => c.Id);
        var direct = grants.Select(g => g.CategoryId).ToHashSet();
        var descendants = grants.Where(g => g.IncludeDescendants).Select(g => g.CategoryId).ToHashSet();
        var allowed = new List<Guid>();
        foreach (var leaf in categories.Where(c => c.IsActive))
        {
            var seen = new HashSet<Guid>(); var valid = true; var granted = false;
            for (Guid? current = leaf.Id; current.HasValue;)
            {
                byId.TryGetValue(current.Value, out var category);
                if (!seen.Add(current.Value) || category is null || !category.IsActive) { valid = false; break; }
                if (current == leaf.Id ? direct.Contains(current.Value) : descendants.Contains(current.Value)) granted = true;
                current = category.ParentId;
            }
            if (valid && granted) allowed.Add(leaf.Id);
        }
        return allowed;
    }

    public async Task EnsureAllowedAsync(Guid userId, Guid categoryId, CancellationToken ct)
    {
        if (!await IsAllowedAsync(userId, categoryId, ct))
            throw new BusinessRuleException("Продаж товарів цієї категорії не дозволено. Зверніться до адміністратора магазину.");
    }

    public async Task<bool> CanClassifyUnmappedImportsAsync(Guid userId, CancellationToken ct)
    {
        var allowed = (await AllowedIdsAsync(userId, ct)).ToHashSet();
        var active = await db.Categories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync(ct);
        return active.Count > 0 && active.All(allowed.Contains);
    }
}
