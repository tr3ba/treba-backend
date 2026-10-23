using System.Security.Cryptography;
using System.Text;
using Application.Common;
using Contracts.Telegram;
using Domain.Entities;
using Domain.Entities.Telegram;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Telegram;

public sealed partial class ProductImportService
{
    public async Task<ImportPreviewDto> SelectCategoryAsync(Guid userId, Guid draftId, Guid categoryId, CancellationToken ct)
    {
        await EnsureSellerAsync(userId, ct);
        var draft = await RequireDraft(userId, draftId, ct);
        await EnsureCategoryChangeAsync(userId, draft, ct);
        await access.EnsureAllowedAsync(userId, categoryId, ct);
        draft.CategoryId = categoryId; draft.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(draft, ct);
    }

    public async Task<ImportPreviewDto> CreateCategoryAsync(Guid userId, Guid draftId, CreateImportCategoryRequest request, CancellationToken ct)
    {
        var name = string.Join(' ', (request.Name ?? "").Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 1 or > 200) throw new ArgumentException("Укажіть назву категорії до 200 символів.");
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await EnsureSellerAsync(userId, ct);
        var draft = await RequireDraft(userId, draftId, ct);
        await EnsureCategoryChangeAsync(userId, draft, ct);
        await access.EnsureAllowedAsync(userId, request.ParentId, ct);
        var parent = await db.Categories.SingleAsync(c => c.Id == request.ParentId && c.IsActive, ct);
        if (parent.Level >= 9) throw new BusinessRuleException("Досягнуто максимальної глибини категорій.");
        var key = NormalizeBrand(name);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"import-category:{parent.Id}:{key}"));
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [BitConverter.ToInt64(digest, 0)], ct);
        var children = await db.Categories.Where(c => c.ParentId == parent.Id).ToListAsync(ct);
        var matches = children.Where(c => NormalizeBrand(c.Name) == key).ToList();
        if (matches.Count > 1 || matches.Any(c => !c.IsActive)) throw new BusinessRuleException("Категорія вимкнена або має дублікати. Зверніться до адміністратора.");
        var category = matches.SingleOrDefault();
        if (category is null)
        {
            category = new Category { Id = Guid.NewGuid(), ParentId = parent.Id, Name = name,
                Slug = "import-category-" + Convert.ToHexString(digest)[..24].ToLowerInvariant(), IsActive = true, Level = parent.Level + 1 };
            db.Categories.Add(category);
            // A seller with direct grants also keeps access to the category they just created.
            var sellerId = await db.Sellers.Where(s => s.UserId == userId).Select(s => s.Id).SingleAsync(ct);
            db.SellerCategoryGrants.Add(new SellerCategoryGrant { SellerId = sellerId, CategoryId = category.Id, IncludeDescendants = true });
        }
        else await access.EnsureAllowedAsync(userId, category.Id, ct);
        draft.CategoryId = category.Id; draft.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await ToDtoAsync(draft, ct);
    }

    private async Task EnsureCategoryChangeAsync(Guid userId, ProductImportDraft draft, CancellationToken ct)
    {
        if (draft.SubmittedAt is not null) throw new BusinessRuleException("Цей імпорт уже завершено.");
        if (!draft.SourceCategoryKey.StartsWith("u:", StringComparison.Ordinal) || !await access.CanClassifyUnmappedImportsAsync(userId, ct))
            throw new BusinessRuleException("Категорія джерела вже налаштована або вибір категорії недоступний.");
        if (await db.RozetkaCategoryMappings.AnyAsync(m => m.ExternalKey == draft.SourceCategoryKey.Substring(2), ct))
            throw new BusinessRuleException("Зіставлення категорії змінилося. Оновіть попередній перегляд.");
    }
}
