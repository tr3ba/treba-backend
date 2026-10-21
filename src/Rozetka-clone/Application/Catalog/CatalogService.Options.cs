using Contracts.Catalog;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Contracts.Admin.Settings;

namespace Application.Catalog;

public sealed partial class CatalogService
{
    private async Task<Guid[]> CategoryBranchAsync(Guid root, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
        var result = new HashSet<Guid> { root };
        var pending = new Queue<Guid>(); pending.Enqueue(root);
        while (pending.TryDequeue(out var parent))
            foreach (var child in categories.Where(c => c.ParentId == parent))
                if (result.Add(child.Id)) pending.Enqueue(child.Id);
        return result.ToArray();
    }

    private async Task<HashSet<Guid>> CategoryAncestorsAsync(Guid id, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);
        var result = new HashSet<Guid>();
        Guid? current = id;
        while (current.HasValue && result.Add(current.Value)) current = categories.GetValueOrDefault(current.Value);
        return result;
    }

    public async Task<CatalogFilterOptionsDto> FilterOptionsAsync(Guid? sellerUserId, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var byId = categories.ToDictionary(c => c.Id);
        string Path(Guid id)
        {
            var names = new List<string>(); var seen = new HashSet<Guid>();
            while (seen.Add(id) && byId.TryGetValue(id, out var item))
            {
                names.Add(item.Name);
                if (!item.ParentId.HasValue) break;
                id = item.ParentId.Value;
            }
            names.Reverse(); return string.Join(" → ", names);
        }
        return new(categories.Select(c => new CatalogLookupDto(c.Id, Path(c.Id))).OrderBy(c => c.Name).ToList(),
            await db.Brands.AsNoTracking().OrderBy(b => b.Name).Select(b => new CatalogLookupDto(b.Id, b.Name)).ToListAsync(ct),
            await db.Sellers.AsNoTracking().Where(s => !sellerUserId.HasValue || s.UserId == sellerUserId.Value)
                .OrderBy(s => s.CompanyName).Select(s => new CatalogLookupDto(s.Id, s.CompanyName)).ToListAsync(ct));
    }

    public async Task<IReadOnlyList<CatalogAttributeDefinitionDto>> AttributeDefinitionsAsync(Guid categoryId, CancellationToken ct)
    {
        var ids = await CategoryAncestorsAsync(categoryId, ct);
        var attributes = await db.Attributes.AsNoTracking().Where(a => ids.Contains(a.CategoryId)).OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct);
        var attributeIds = attributes.Select(a => a.Id).ToArray();
        var options = await db.AttributeOptions.AsNoTracking().Where(o => attributeIds.Contains(o.AttributeId)).OrderBy(o => o.SortOrder).ThenBy(o => o.Value).ToListAsync(ct);
        return attributes.Select(a => new CatalogAttributeDefinitionDto(a.Id, a.Name, a.Type.ToString(), a.Unit, a.IsRequired,
            options.Where(o => o.AttributeId == a.Id).Select(o => new CatalogLookupDto(o.Id, o.DisplayValue ?? o.Value)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<ProductModerationDto>> ModerationHistoryAsync(Guid productId, CancellationToken ct)
    {
        var logs = await db.AdminAuditLogs.AsNoTracking().Where(d => d.EntityType == "Product" && d.EntityId == productId && d.Action == "Moderation")
            .OrderByDescending(d => d.OccurredAt).ThenByDescending(d => d.Id).Take(100).ToListAsync(ct);
        return logs.Select(log => {
            var changes = JsonSerializer.Deserialize<List<AuditChangeDto>>(log.ChangesJson) ?? [];
            return new ProductModerationDto(log.Id, log.OccurredAt, log.ActorName, changes.FirstOrDefault(c => c.Field == "Decision")?.After ?? "", changes.FirstOrDefault(c => c.Field == "Reason")?.After);
        }).ToList();
    }
}
