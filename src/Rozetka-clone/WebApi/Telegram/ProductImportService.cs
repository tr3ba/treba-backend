using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using Application.Abstractions;
using Application.Catalog;
using Application.Common;
using Application.Products;
using Contracts.Catalog;
using Contracts.Telegram;
using Domain.Entities.Product;
using Domain.Entities.Telegram;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Telegram;

public sealed partial class ProductImportService(ApplicationDbContext db, IMarketplaceSourceClient source, MarketplaceProductParser parser,
    SellerCategoryAccess access, CatalogService catalog, IProductService products, IProductMediaStorage media, TimeProvider clock)
{
    public async Task EnsureSellerAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.Sellers.AnyAsync(s => s.UserId == userId && s.Status == SellerStatus.ACTIVE, ct))
            throw new BusinessRuleException("Імпорт доступний лише активному продавцю.");
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsDeleted && u.Status == Domain.Entities.Users.UserStatus.Active && u.Role != null && u.Role.Name == Domain.Entities.Users.Roles.Seller, ct))
            throw new BusinessRuleException("Доступ облікового запису продавця змінився.");
    }

    public async Task<SellerImportOptionsDto> OptionsAsync(Guid userId, CancellationToken ct)
    {
        await EnsureSellerAsync(userId, ct);
        var allowed = await access.AllowedIdsAsync(userId, ct);
        var categoryOptions = (await catalog.FilterOptionsAsync(userId, ct)).Categories.Where(c => allowed.Contains(c.Id)).ToList();
        return new(categoryOptions,
            await db.Brands.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => new CatalogLookupDto(b.Id, b.Name)).ToListAsync(ct),
            await db.Stores.AsNoTracking().Where(s => s.IsActive && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == userId && seller.Status == SellerStatus.ACTIVE)).OrderBy(s => s.Name).Select(s => new CatalogLookupDto(s.Id, s.Name)).ToListAsync(ct),
            await access.CanClassifyUnmappedImportsAsync(userId, ct));
    }

    public async Task<CatalogLookupDto> CreateBrandAsync(Guid userId, Guid draftId, string name, CancellationToken ct)
    {
        name = string.Join(' ', name.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 1 or > 200) throw new ArgumentException("Укажіть назву бренду до 200 символів.");
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await EnsureSellerAsync(userId, ct);
        var draft = await RequireDraft(userId, draftId, ct);
        if (draft.SubmittedAt is not null) throw new BusinessRuleException("Цей імпорт уже завершено.");
        await access.EnsureAllowedAsync(userId, draft.CategoryId, ct);
        if (!await db.Stores.AnyAsync(s => s.IsActive && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == userId && seller.Status == SellerStatus.ACTIVE), ct))
            throw new BusinessRuleException("У продавця немає доступного магазину.");
        var key = NormalizeBrand(name);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("seller-import-brand:" + key));
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [BitConverter.ToInt64(digest, 0)], ct);
        var known = (await db.Brands.AsNoTracking().ToListAsync(ct)).FirstOrDefault(b => NormalizeBrand(b.Name) == key);
        if (known is not null)
        {
            if (!known.IsActive) throw new BusinessRuleException("Цей бренд вимкнено. Виберіть інший бренд або зверніться до адміністратора.");
            if (transaction is not null) await transaction.CommitAsync(ct);
            return new(known.Id, known.Name);
        }
        var slug = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", "-").Trim('-');
        if (slug.Length > 180) slug = slug[..180];
        var brand = new Domain.Entities.Brand { Id = Guid.NewGuid(), Name = name, Slug = (slug.Length == 0 ? "brand" : slug) + "-" + Convert.ToHexString(digest)[..16].ToLowerInvariant(), IsActive = true };
        db.Brands.Add(brand);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(brand.Id, brand.Name);
    }

    private static string NormalizeBrand(string name) => string.Join(' ', name.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public async Task<ImportPreviewDto> PreviewAsync(Guid userId, string url, CancellationToken ct, Guid? categoryId = null)
    {
        var uri = MarketplaceSourceClient.ProductUri(url); var now = clock.GetUtcNow();
        await EnsureSellerAsync(userId, ct);
        if (await db.ProductImportDrafts.CountAsync(d => d.UserId == userId && d.ExpiresAt > now && d.SubmittedAt == null, ct) >= 50)
            throw new BusinessRuleException("Забагато незавершених імпортів. Завершіть поточні або спробуйте пізніше.");
        var data = parser.Parse(uri, await source.GetPageAsync(uri, ct));
        var mapping = await db.RozetkaCategoryMappings.AsNoTracking().SingleOrDefaultAsync(m => m.ExternalKey == data.CategoryKey, ct);
        var independentClassification = mapping is null;
        if (mapping is not null)
        {
            if (categoryId.HasValue && categoryId != mapping.CategoryId) throw new BusinessRuleException("Категорія джерела вже налаштована. Виберіть її або залиште автоматичне визначення.");
            categoryId = mapping.CategoryId;
        }
        else
        {
            if (!await access.CanClassifyUnmappedImportsAsync(userId, ct))
                throw new BusinessRuleException("Категорія джерела " + data.CategoryKey + " ще не зіставлена з каталогом Treba. Зверніться до адміністратора.");
            if (!categoryId.HasValue)
            {
                var allowed = await access.AllowedIdsAsync(userId, ct);
                var candidates = await db.Categories.AsNoTracking().Where(c => c.IsActive && allowed.Contains(c.Id)).ToListAsync(ct);
                categoryId = ImportCategoryResolver.Resolve(data, candidates);
            }
            if (!categoryId.HasValue) throw new BusinessRuleException("Виберіть категорію Treba під посиланням і повторіть завантаження. Категорія джерела: " + data.CategoryName + ".");
        }
        await access.EnsureAllowedAsync(userId, categoryId!.Value, ct);
        var imported = await db.ProductImportDrafts.Where(d => d.UserId == userId && d.SourceProductId == data.Id && d.SubmittedAt != null
            && db.Products.Any(p => p.Id == d.ProductId && p.Status != ProductStatus.ARCHIVED)).OrderByDescending(d => d.SubmittedAt).FirstOrDefaultAsync(ct);
        if (imported is not null) return await ToDtoAsync(imported, ct);
        var details = data.Characteristics.Count == 0 ? "" : "\n\nХарактеристики джерела:\n" + string.Join('\n', data.Characteristics.Select(c => c.Name + ": " + c.Value));
        var description = data.Description + details;
        var draft = new ProductImportDraft { UserId = userId, SourceProductId = data.Id, SourceUrl = data.Url, Name = data.Name,
            Description = description[..Math.Min(10000, description.Length)], BrandName = data.BrandName, SourceCategoryKey = (independentClassification ? "u:" : "") + data.CategoryKey,
            SourceCategoryName = data.CategoryName, CategoryId = categoryId.Value, SourcePrice = data.Price,
            ImagesJson = JsonSerializer.Serialize(data.Images), CharacteristicsJson = JsonSerializer.Serialize(data.Characteristics), CreatedAt = now, ExpiresAt = now.AddHours(1) };
        db.ProductImportDrafts.Add(draft); await db.SaveChangesAsync(ct);
        return await ToDtoAsync(draft, ct);
    }
    public async Task<ImportPreviewDto> GetAsync(Guid userId, Guid id, CancellationToken ct)
    { var draft = await RequireDraft(userId, id, ct); await EnsureSellerAsync(userId, ct); return await ToDtoAsync(draft, ct); }

    public async Task<Guid> SubmitAsync(Guid userId, Guid id, SubmitImportRequest request, CancellationToken ct)
    {
        await EnsureSellerAsync(userId, ct);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var draft = await RequireDraft(userId, id, ct);
        if (draft.SubmittedAt is not null && draft.ProductId is { } completed) return completed;
        if (db.Database.IsNpgsql())
        {
            var sourceLock = SHA256.HashData(Encoding.UTF8.GetBytes($"import-product:{userId}:{draft.SourceProductId}"));
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [BitConverter.ToInt64(sourceLock, 0)], ct);
        }
        var imported = await db.ProductImportDrafts.AsNoTracking().Where(d => d.UserId == userId && d.SourceProductId == draft.SourceProductId
            && d.SubmittedAt != null && db.Products.Any(p => p.Id == d.ProductId && p.Status != ProductStatus.ARCHIVED))
            .Select(d => d.ProductId).FirstOrDefaultAsync(ct);
        if (imported.HasValue)
        {
            draft.ProductId = imported; draft.SubmittedAt = clock.GetUtcNow(); draft.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return imported.Value;
        }
        var independentClassification = draft.SourceCategoryKey.StartsWith("u:", StringComparison.Ordinal);
        var externalKey = independentClassification ? draft.SourceCategoryKey[2..] : draft.SourceCategoryKey;
        var mapping = await db.RozetkaCategoryMappings.AsNoTracking().SingleOrDefaultAsync(m => m.ExternalKey == externalKey, ct);
        if (independentClassification ? mapping is not null && mapping.CategoryId != draft.CategoryId || !await access.CanClassifyUnmappedImportsAsync(userId, ct) : mapping?.CategoryId != draft.CategoryId)
            throw new BusinessRuleException("Зіставлення категорії або дозволи змінилися. Створіть попередній перегляд знову.");
        await access.EnsureAllowedAsync(userId, draft.CategoryId, ct);
        if (!await db.Stores.AnyAsync(s => s.Id == request.StoreId && s.IsActive && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == userId && seller.Status == SellerStatus.ACTIVE), ct))
            throw new BusinessRuleException("Виберіть доступний магазин продавця.");
        var identifiers = ReadIdentifiers(draft);
        if (string.IsNullOrWhiteSpace(request.ManufacturerPartNumber)) request.ManufacturerPartNumber = identifiers.PartNumber;
        if (string.IsNullOrWhiteSpace(request.Barcode)) request.Barcode = identifiers.Barcode;
        var duplicates = await new CatalogDuplicateService(db).FindAsync(userId, request.BrandId, request.ManufacturerPartNumber, request.Barcode, null, ct);
        if (duplicates.Count > 0 && !request.DuplicateConfirmed) throw new BusinessRuleException("Схожий товар уже є у вашому каталозі. Відкрийте його або підтвердьте створення окремої картки.");
        draft.Version = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        var written = new List<(Guid ProductId, string FileName)>();
        try
        {
            var product = await catalog.SaveAsync(null, request.StoreId, new SaveCatalogProductRequest {
                Name = request.Name, Slug = "import-" + draft.SourceProductId.Replace(':', '-') + "-" + draft.Id.ToString("N")[..12], CategoryId = draft.CategoryId,
                BrandId = request.BrandId, StoreId = request.StoreId, Description = request.Description, ManufacturerPartNumber = request.ManufacturerPartNumber,
                ShortDescription = request.Description[..Math.Min(500, request.Description.Length)], Sku = request.Sku,
                Price = request.Price, StockQuantity = request.StockQuantity, Attributes = request.Attributes,
                Variants = [new() { Name = "Основний", Sku = request.Sku, Barcode = request.Barcode, Price = request.Price, StockQuantity = request.StockQuantity }] }, ct)
                ?? throw new BusinessRuleException("Не вдалося створити товар.");
            var urls = JsonSerializer.Deserialize<List<string>>(draft.ImagesJson) ?? [];
            for (var index = 0; index < urls.Count; index++)
            {
                var bytes = await source.GetImageAsync(MarketplaceSourceClient.ImageUri(urls[index]), ct);
                if (bytes.Length is <= 0 or > 5 * 1024 * 1024) throw new BusinessRuleException("Фотографія джерела перевищує допустимий розмір.");
                var extension = ImageExtension(bytes) ?? throw new BusinessRuleException("Фотографія джерела має непідтримуваний формат.");
                var fileName = Guid.NewGuid().ToString("N") + extension;
                written.Add((product.Id, fileName)); // Also clean up a partially written file if storage fails.
                await media.SaveAsync(product.Id, fileName, bytes, ct);
                db.ProductImages.Add(new ProductImage { Id = Guid.NewGuid(), ProductId = product.Id, ImageUrl = $"/uploads/products/{product.Id:N}/{fileName}",
                    AltText = request.Name, SortOrder = index, IsMain = index == 0 });
            }
            draft.ProductId = product.Id; draft.SubmittedAt = clock.GetUtcNow(); draft.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            if (!await products.SubmitForModerationAsync(product.Id, ct)) throw new BusinessRuleException("Не вдалося надіслати товар на модерацію.");
            if (transaction is not null) await transaction.CommitAsync(ct);
            return product.Id;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            foreach (var file in written)
                try { await media.DeleteAsync(file.ProductId, file.FileName, CancellationToken.None); } catch (IOException) { }
            throw;
        }
    }
    private async Task<ProductImportDraft> RequireDraft(Guid userId, Guid id, CancellationToken ct)
    {
        var draft = await db.ProductImportDrafts.SingleOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (draft is null) throw new KeyNotFoundException("Попередній перегляд не знайдено.");
        if (draft.SubmittedAt is null && draft.ExpiresAt <= clock.GetUtcNow()) throw new BusinessRuleException("Термін дії попереднього перегляду минув. Повторіть імпорт посилання.");
        return draft;
    }
    private async Task<ImportPreviewDto> ToDtoAsync(ProductImportDraft draft, CancellationToken ct)
    {
        var identifiers = ReadIdentifiers(draft);
        var brands = await db.Brands.AsNoTracking().Where(b => b.IsActive).ToListAsync(ct);
        var brand = brands.FirstOrDefault(b => NormalizeBrand(b.Name) == NormalizeBrand(draft.BrandName));
        if (brand is null && string.IsNullOrWhiteSpace(draft.BrandName))
        {
            var matches = brands.Where(b => b.Name.Length >= 2 && System.Text.RegularExpressions.Regex.IsMatch(draft.Name,
                @"(?<![\p{L}\p{Nd}])" + System.Text.RegularExpressions.Regex.Escape(b.Name) + @"(?![\p{L}\p{Nd}])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
            if (matches.Count == 1) brand = matches[0];
        }
        var brandId = brand?.Id ?? Guid.Empty;
        var duplicates = await new CatalogDuplicateService(db).FindAsync(draft.UserId, brandId, identifiers.PartNumber, identifiers.Barcode, draft.ProductId, ct);
        return new(draft.Id, draft.SourceUrl, draft.Name,
        draft.Description, string.IsNullOrWhiteSpace(draft.BrandName) ? brand?.Name ?? "" : draft.BrandName, draft.SourceCategoryName, draft.CategoryId,
        await db.Categories.Where(c => c.Id == draft.CategoryId).Select(c => c.Name).SingleAsync(ct), draft.SourcePrice,
        JsonSerializer.Deserialize<List<string>>(draft.ImagesJson) ?? [], JsonSerializer.Deserialize<List<ImportedCharacteristicDto>>(draft.CharacteristicsJson) ?? [],
        draft.ExpiresAt, draft.ProductId, draft.SubmittedAt) { BrandId = brandId == Guid.Empty ? null : brandId, ManufacturerPartNumber = identifiers.PartNumber, Barcode = identifiers.Barcode, Duplicates = duplicates,
            CanClassifyCategory = draft.SubmittedAt is null && draft.SourceCategoryKey.StartsWith("u:", StringComparison.Ordinal) && await access.CanClassifyUnmappedImportsAsync(draft.UserId, ct) };
    }
    private static (string PartNumber, string Barcode) ReadIdentifiers(ProductImportDraft draft)
    {
        var values = JsonSerializer.Deserialize<List<ImportedCharacteristicDto>>(draft.CharacteristicsJson) ?? [];
        string Find(params string[] names) => values.FirstOrDefault(v => names.Contains(CatalogDuplicateService.Normalize(v.Name)))?.Value ?? "";
        var part = Find("АРТИКУЛПРОИЗВОДИТЕЛЯ", "АРТИКУЛВИРОБНИКА", "MPN", "КОДВИРОБНИКА");
        var barcode = Find("ШТРИХКОД", "EAN", "GTIN", "EAN13");
        return (part[..Math.Min(100, part.Length)], CatalogDuplicateService.Normalize(barcode)[..Math.Min(100, CatalogDuplicateService.Normalize(barcode).Length)]);
    }
    public static string? ImageExtension(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 137,80,78,71,13,10,26,10 })) return ".png";
        if (bytes.AsSpan().StartsWith(new byte[] { 255,216,255 })) return ".jpg";
        if (bytes.Length >= 12 && bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8)) return ".webp";
        if (bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8)) return ".gif";
        return null;
    }
}
