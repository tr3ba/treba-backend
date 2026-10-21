using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Entities;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Persistence;
using Application.Catalog;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Administration;

public sealed record RozetkaCategorySnapshot(string Version, string Source, IReadOnlyList<RozetkaCategoryEntry> Categories);
public sealed record RozetkaCategoryEntry(string Key, string? ParentKey, string Name, string SourceUrl, int SortOrder,
    string? ExistingSlug, string? ExistingName, string? ImageUrl, string? ImageKind);
public sealed record CatalogSnapshotResult(int AddedCategories, int AddedMappings, int UpdatedImages, int FullCatalogSellers);

public static class RozetkaCatalogDeployment
{
    public static RozetkaCategorySnapshot LoadSnapshot()
    {
        using var stream = typeof(RozetkaCatalogDeployment).Assembly.GetManifestResourceStream("WebApi.CatalogData.rozetka-categories.json")
            ?? throw new InvalidOperationException("The reviewed category snapshot is missing.");
        return JsonSerializer.Deserialize<RozetkaCategorySnapshot>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The category snapshot is empty.");
    }

    public static void Validate(RozetkaCategorySnapshot snapshot)
    {
        var seen = new HashSet<string>(); var slugs = new HashSet<string>();
        foreach (var row in snapshot.Categories)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(row.Key, "^c[1-9][0-9]{0,11}$") || !seen.Add(row.Key)
                || row.ParentKey is not null && !seen.Contains(row.ParentKey) || row.ParentKey == row.Key
                || string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 200 || row.SortOrder < 0
                || row.ExistingSlug is not null && !slugs.Add(row.ExistingSlug)
                || !Uri.TryCreate(row.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != "https" || !SourceHost(source.Host)
                || !string.IsNullOrEmpty(source.UserInfo)
                || row.ImageUrl is not null && (!Uri.TryCreate(row.ImageUrl, UriKind.Absolute, out var image) || image.Scheme != "https"
                    || !SourceHost(image.Host) || !string.IsNullOrEmpty(image.UserInfo) || row.ImageUrl.Length > 1000))
                throw new InvalidOperationException("Invalid category snapshot entry: " + row.Key);
        }
        if (seen.Count == 0) throw new InvalidOperationException("The category snapshot has no categories.");
    }

    private static bool SourceHost(string host) => host == "rozetka.com.ua" || host.EndsWith(".rozetka.com.ua", StringComparison.OrdinalIgnoreCase);
    private static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("treba:rozetka-category:" + key)).AsSpan(0, 16));
    private static bool ActiveBranch(Category? category, IReadOnlyDictionary<Guid, Category> categories)
    {
        var seen = new HashSet<Guid>();
        while (category is not null)
        {
            if (!category.IsActive || !seen.Add(category.Id)) return false;
            if (category.ParentId is not { } parent) return true;
            if (!categories.TryGetValue(parent, out category)) return false;
        }
        return true;
    }

    public static async Task<CatalogSnapshotResult> ApplyAsync(ApplicationDbContext db, RozetkaCategorySnapshot snapshot, CancellationToken ct)
    {
        Validate(snapshot);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7624618102101)", ct);
        var categories = await db.Categories.ToListAsync(ct);
        var byId = categories.ToDictionary(c => c.Id); var bySlug = categories.ToDictionary(c => c.Slug);
        var mappings = await db.RozetkaCategoryMappings.ToListAsync(ct); var byKey = mappings.ToDictionary(m => m.ExternalKey);
        var activeSellers = await db.Sellers.Where(s => s.Status == SellerStatus.ACTIVE && db.Users.Any(u => u.Id == s.UserId && !u.IsDeleted
            && u.Status == UserStatus.Active && u.Role != null && u.Role.Name == Roles.Seller)).ToListAsync(ct);
        var access = new SellerCategoryAccess(db); var fullSellers = new List<Seller>();
        foreach (var seller in activeSellers)
            if (await access.CanClassifyUnmappedImportsAsync(seller.UserId, ct)) fullSellers.Add(seller);
        var resolved = new Dictionary<string, Category>(); var added = new List<Category>(); var newMappings = 0; var images = 0;
        var nextSort = categories.GroupBy(c => c.ParentId ?? Guid.Empty).ToDictionary(g => g.Key, g => g.Max(c => c.SortOrder) + 1);
        foreach (var row in snapshot.Categories)
        {
            Category? category = null;
            if (byKey.TryGetValue(row.Key, out var mapping)) category = byId[mapping.CategoryId];
            else if (row.ExistingSlug is not null) bySlug.TryGetValue(row.ExistingSlug, out category);
            var slug = "rz-" + row.Key;
            if (category is null && bySlug.TryGetValue(slug, out category) && category.Id != Id(row.Key))
                throw new InvalidOperationException("A reserved source category slug is already in use: " + row.Key);
            if (category is null)
            {
                var parent = row.ParentKey is null ? null : resolved[row.ParentKey];
                var sortKey = parent?.Id ?? Guid.Empty;
                category = new Category { Id = Id(row.Key), ParentId = parent?.Id, Name = row.Name, Slug = slug, IsActive = ActiveBranch(parent, byId),
                    Level = parent is null ? 0 : parent.Level + 1, SortOrder = nextSort.GetValueOrDefault(sortKey) };
                nextSort[sortKey] = category.SortOrder + 1;
                db.Categories.Add(category); categories.Add(category); byId.Add(category.Id, category); bySlug.Add(category.Slug, category); added.Add(category);
            }
            // Keep identities, parent relationships, custom edits and disabled categories.
            if (category.Slug == row.ExistingSlug && category.Name == row.ExistingName) category.Name = row.Name;
            if (string.IsNullOrWhiteSpace(category.ImageUrl) && row.ImageUrl is not null) { category.ImageUrl = row.ImageUrl; images++; }
            if (mapping is null)
            {
                mapping = new RozetkaCategoryMapping { Id = Id("mapping:" + row.Key), ExternalKey = row.Key, CategoryId = category.Id };
                db.RozetkaCategoryMappings.Add(mapping); byKey.Add(row.Key, mapping); newMappings++;
            }
            resolved.Add(row.Key, category);
        }
        // Legacy Treba-only groupings keep their URLs; use a matching category, child or parent photograph.
        static string NameKey(string name) => string.Concat(name.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit));
        var photosByName = resolved.Values.Where(c => !string.IsNullOrWhiteSpace(c.ImageUrl)).GroupBy(c => NameKey(c.Name))
            .Where(g => g.Select(c => c.ImageUrl).Distinct().Count() == 1).ToDictionary(g => g.Key, g => g.First().ImageUrl);
        foreach (var category in categories.Where(c => string.IsNullOrWhiteSpace(c.ImageUrl)))
            if (photosByName.TryGetValue(NameKey(category.Name), out var photo)) { category.ImageUrl = photo; images++; }
        foreach (var row in snapshot.Categories.Where(r => r.ExistingName is not null))
            if (resolved[row.Key].ImageUrl is { } photo)
                foreach (var category in categories.Where(c => string.IsNullOrWhiteSpace(c.ImageUrl) && NameKey(c.Name) == NameKey(row.ExistingName!)))
                { category.ImageUrl = photo; images++; }
        var children = categories.GroupBy(c => c.ParentId ?? Guid.Empty).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var category in categories.OrderByDescending(c => c.Level).Where(c => string.IsNullOrWhiteSpace(c.ImageUrl)))
            if (children.GetValueOrDefault(category.Id)?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.ImageUrl))?.ImageUrl is { } photo) { category.ImageUrl = photo; images++; }
        // Treba-only travel and promotion groupings have no equivalent source branch; use related representative photos.
        foreach (var (legacySlug, sourceKey) in new[] { ("tours", "c90423"), ("sale", "c83850") })
            if (bySlug.TryGetValue(legacySlug, out var category) && string.IsNullOrWhiteSpace(category.ImageUrl)
                && resolved.TryGetValue(sourceKey, out var sourceCategory) && sourceCategory.ImageUrl is { } photo)
            { category.ImageUrl = photo; images++; }
        foreach (var category in categories.OrderBy(c => c.Level).Where(c => string.IsNullOrWhiteSpace(c.ImageUrl)))
            if (category.ParentId is { } parentId && byId[parentId].ImageUrl is { } photo) { category.ImageUrl = photo; images++; }
        await db.SaveChangesAsync(ct);
        // Preserve full-catalog sellers' ability to import as new branches are added. Restricted sellers inherit only their existing grants.
        foreach (var seller in fullSellers)
        {
            var allowed = (await access.AllowedIdsAsync(seller.UserId, ct)).ToHashSet();
            var missing = added.Where(c => c.IsActive && !allowed.Contains(c.Id)).Select(c => c.Id).ToHashSet();
            foreach (var category in added.Where(c => missing.Contains(c.Id) && (c.ParentId is null || !missing.Contains(c.ParentId.Value))))
                db.SellerCategoryGrants.Add(new() { SellerId = seller.Id, CategoryId = category.Id, IncludeDescendants = true });
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(added.Count, newMappings, images, fullSellers.Count);
    }
}
