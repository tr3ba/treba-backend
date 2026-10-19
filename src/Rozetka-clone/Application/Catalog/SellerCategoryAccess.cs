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
        var allowed = new List<Guid>();
        foreach (var leaf in categories.Where(c => c.IsActive))
        {
            var seen = new HashSet<Guid>(); var valid = true; var granted = false;
            for (Guid? current = leaf.Id; current.HasValue;)
            {
                var category = categories.FirstOrDefault(c => c.Id == current);
                if (!seen.Add(current.Value) || category is null || !category.IsActive) { valid = false; break; }
                if (grants.Any(g => g.CategoryId == current && (current == leaf.Id || g.IncludeDescendants))) granted = true;
                current = category.ParentId;
            }
            if (valid && granted) allowed.Add(leaf.Id);
        }
        return allowed;
    }

    public async Task EnsureAllowedAsync(Guid userId, Guid categoryId, CancellationToken ct)
    {
        if (!await IsAllowedAsync(userId, categoryId, ct))
            throw new BusinessRuleException("Продажа товаров этой категории не разрешена. Обратитесь к администратору магазина.");
    }
}
