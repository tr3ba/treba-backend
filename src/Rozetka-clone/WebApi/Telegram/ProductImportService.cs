using System.Text.Json;
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

public sealed class ProductImportService(ApplicationDbContext db, IRozetkaSourceClient source, RozetkaProductParser parser,
    SellerCategoryAccess access, CatalogService catalog, IProductService products, IProductMediaStorage media, TimeProvider clock)
{
    public async Task<ImportPreviewDto> PreviewAsync(Guid userId, string url, CancellationToken ct)
    {
        var uri = RozetkaSourceClient.ProductUri(url); var now = clock.GetUtcNow();
        if (!await db.Sellers.AnyAsync(s => s.UserId == userId && s.Status == SellerStatus.ACTIVE, ct))
            throw new BusinessRuleException("Импорт доступен только активному продавцу.");
        if (await db.ProductImportDrafts.CountAsync(d => d.UserId == userId && d.ExpiresAt > now && d.SubmittedAt == null, ct) >= 10)
            throw new BusinessRuleException("Слишком много незавершённых импортов. Завершите текущие или попробуйте позже.");
        var data = parser.Parse(uri, await source.GetPageAsync(uri, ct));
        var mapping = await db.RozetkaCategoryMappings.AsNoTracking().SingleOrDefaultAsync(m => m.ExternalKey == data.CategoryKey, ct);
        if (mapping is null) throw new BusinessRuleException("Категория Rozetka " + data.CategoryKey + " ещё не сопоставлена с каталогом Treba. Обратитесь к администратору.");
        await access.EnsureAllowedAsync(userId, mapping.CategoryId, ct);
        if (await db.ProductImportDrafts.AnyAsync(d => d.UserId == userId && d.SourceProductId == data.Id && d.SubmittedAt != null, ct))
            throw new BusinessRuleException("Этот товар уже импортирован в ваш каталог. Откройте его в списке товаров.");
        var details = data.Characteristics.Count == 0 ? "" : "\n\nХарактеристики источника:\n" + string.Join('\n', data.Characteristics.Select(c => c.Name + ": " + c.Value));
        var description = data.Description + details;
        var draft = new ProductImportDraft { UserId = userId, SourceProductId = data.Id, SourceUrl = data.Url, Name = data.Name,
            Description = description[..Math.Min(10000, description.Length)], BrandName = data.BrandName, SourceCategoryKey = data.CategoryKey,
            SourceCategoryName = data.CategoryName, CategoryId = mapping.CategoryId, SourcePrice = data.Price,
            ImagesJson = JsonSerializer.Serialize(data.Images), CharacteristicsJson = JsonSerializer.Serialize(data.Characteristics), CreatedAt = now, ExpiresAt = now.AddHours(1) };
        db.ProductImportDrafts.Add(draft); await db.SaveChangesAsync(ct);
        return await ToDtoAsync(draft, ct);
    }
    public async Task<ImportPreviewDto> GetAsync(Guid userId, Guid id, CancellationToken ct) => await ToDtoAsync(await RequireDraft(userId, id, ct), ct);

    public async Task<Guid> SubmitAsync(Guid userId, Guid id, SubmitImportRequest request, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var draft = await RequireDraft(userId, id, ct);
        if (draft.SubmittedAt is not null && draft.ProductId is { } completed) return completed;
        var mapping = await db.RozetkaCategoryMappings.AsNoTracking().SingleOrDefaultAsync(m => m.ExternalKey == draft.SourceCategoryKey, ct);
        if (mapping?.CategoryId != draft.CategoryId) throw new BusinessRuleException("Сопоставление категории изменилось. Создайте предпросмотр заново.");
        await access.EnsureAllowedAsync(userId, draft.CategoryId, ct);
        if (!await db.Stores.AnyAsync(s => s.Id == request.StoreId && s.IsActive && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == userId && seller.Status == SellerStatus.ACTIVE), ct))
            throw new BusinessRuleException("Выберите доступный магазин продавца.");
        draft.Version = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        var written = new List<(Guid ProductId, string FileName)>();
        try
        {
            var product = await catalog.SaveAsync(null, request.StoreId, new SaveCatalogProductRequest {
                Name = request.Name, Slug = "import-" + draft.SourceProductId + "-" + draft.Id.ToString("N")[..12], CategoryId = draft.CategoryId,
                BrandId = request.BrandId, StoreId = request.StoreId, Description = request.Description,
                ShortDescription = request.Description[..Math.Min(500, request.Description.Length)], Sku = request.Sku,
                Price = request.Price, StockQuantity = request.StockQuantity, Attributes = request.Attributes,
                Variants = [new() { Name = "Основной", Sku = request.Sku, Price = request.Price, StockQuantity = request.StockQuantity }] }, ct)
                ?? throw new BusinessRuleException("Не удалось создать товар.");
            var urls = JsonSerializer.Deserialize<List<string>>(draft.ImagesJson) ?? [];
            for (var index = 0; index < urls.Count; index++)
            {
                var bytes = await source.GetImageAsync(RozetkaSourceClient.ImageUri(urls[index]), ct);
                if (bytes.Length is <= 0 or > 5 * 1024 * 1024) throw new BusinessRuleException("Фотография источника превышает допустимый размер.");
                var extension = ImageExtension(bytes) ?? throw new BusinessRuleException("Фотография источника имеет неподдерживаемый формат.");
                var fileName = Guid.NewGuid().ToString("N") + extension;
                written.Add((product.Id, fileName)); // Also clean up a partially written file if storage fails.
                await media.SaveAsync(product.Id, fileName, bytes, ct);
                db.ProductImages.Add(new ProductImage { Id = Guid.NewGuid(), ProductId = product.Id, ImageUrl = $"/uploads/products/{product.Id:N}/{fileName}",
                    AltText = request.Name, SortOrder = index, IsMain = index == 0 });
            }
            draft.ProductId = product.Id; draft.SubmittedAt = clock.GetUtcNow(); draft.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            if (!await products.SubmitForModerationAsync(product.Id, ct)) throw new BusinessRuleException("Не удалось отправить товар на модерацию.");
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
        if (draft is null) throw new KeyNotFoundException("Предпросмотр не найден.");
        if (draft.SubmittedAt is null && draft.ExpiresAt <= clock.GetUtcNow()) throw new BusinessRuleException("Предпросмотр истёк. Повторите импорт ссылки.");
        return draft;
    }
    private async Task<ImportPreviewDto> ToDtoAsync(ProductImportDraft draft, CancellationToken ct) => new(draft.Id, draft.SourceUrl, draft.Name,
        draft.Description, draft.BrandName, draft.SourceCategoryName, draft.CategoryId,
        await db.Categories.Where(c => c.Id == draft.CategoryId).Select(c => c.Name).SingleAsync(ct), draft.SourcePrice,
        JsonSerializer.Deserialize<List<string>>(draft.ImagesJson) ?? [], JsonSerializer.Deserialize<List<ImportedCharacteristicDto>>(draft.CharacteristicsJson) ?? [],
        draft.ExpiresAt, draft.ProductId, draft.SubmittedAt);
    public static string? ImageExtension(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 137,80,78,71,13,10,26,10 })) return ".png";
        if (bytes.AsSpan().StartsWith(new byte[] { 255,216,255 })) return ".jpg";
        if (bytes.Length >= 12 && bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8)) return ".webp";
        if (bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8)) return ".gif";
        return null;
    }
}
