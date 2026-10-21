using Domain.Entities;
using Domain.Entities.Telegram;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Administration;

// Idempotent, additive catalog data update, run by the existing deployment preflight, never on import.
public static class ImportCatalogDeployment
{
    public const string CookwareSlug = "home-posud-dlia-pryhotuvannia-izhi";
    public static async Task ApplyAsync(ApplicationDbContext db, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7624618102101)", ct);
        var categories = await db.Categories.ToListAsync(ct);
        var parent = categories.SingleOrDefault(c => c.Slug == CookwareSlug);
        if (parent is { IsActive: true })
        {
            (string Name, string Suffix)[] cookware =
            [
                ("Сковороди", "skovorody"), ("Каструлі", "kastruli"), ("Ковші", "kovshi"),
                ("Сотейники", "soteinyky"), ("Набори посуду для приготування їжі", "nabory-posudu"),
                ("Форми для випікання", "formy-dlia-vypikannia"), ("Дека для випікання", "deka"), ("Кришки для посуду", "kryshky")
            ];
            foreach (var (name, suffix) in cookware)
            {
                var slug = CookwareSlug + "-" + suffix;
                // Preserve operator-created categories and disabled categories; never reactivate or move them.
                if (categories.Any(c => c.Slug == slug || c.ParentId == parent.Id && c.Name == name)) continue;
                var category = new Category { Id = Guid.NewGuid(), ParentId = parent.Id, Name = name, Slug = slug,
                    IsActive = true, Level = parent.Level + 1, SortOrder = categories.Count(c => c.ParentId == parent.Id) };
                db.Categories.Add(category); categories.Add(category);
            }
        }
        // Only reviewed source category keys. Existing administrator mappings remain authoritative.
        foreach (var (externalKey, slug) in new[] { ("c80004", "laptops-noutbuky-ta-pk-noutbuky"), ("c4626754", CookwareSlug + "-skovorody") })
        {
            var category = categories.SingleOrDefault(c => c.Slug == slug);
            if (category is null || !category.IsActive || await db.RozetkaCategoryMappings.AnyAsync(m => m.ExternalKey == externalKey, ct)) continue;
            db.RozetkaCategoryMappings.Add(new RozetkaCategoryMapping { ExternalKey = externalKey, CategoryId = category.Id });
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }
}
