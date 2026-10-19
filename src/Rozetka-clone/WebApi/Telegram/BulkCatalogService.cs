using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Catalog;
using Application.Common;
using Contracts.Catalog;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Telegram;

public sealed class BulkCatalogService(ApplicationDbContext db, IDataProtectionProvider protection, SellerCategoryAccess categories, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Treba.CatalogBulk.v1");
    public async Task<BulkCatalogPreviewDto> PreviewAsync(Guid actorId, Guid? sellerUserId, BulkCatalogRequest request, CancellationToken ct)
    {
        Validator.ValidateObject(request, new(request), true);
        var products = await Products(request.ProductIds, sellerUserId, ct);
        var ids = products.Select(p => p.Id).ToArray();
        var variants = await db.ProductVariants.AsNoTracking().Where(v => ids.Contains(v.ProductId)).OrderBy(v => v.Id).ToListAsync(ct);
        if (variants.Count is < 1 or > 500) throw new BusinessRuleException("Выберите товары, содержащие от 1 до 500 вариантов.");
        var rows = variants.Select(v =>
        {
            var after = decimal.Round(request.Price ?? v.Price * (1 + (request.PricePercent ?? 0) / 100), 2, MidpointRounding.AwayFromZero);
            return new BulkCatalogRowDto(v.ProductId, v.Id, products.Single(p => p.Id == v.ProductId).Name, v.Sku, v.Price, after,
                v.StockQuantity, request.StockQuantity ?? v.StockQuantity, v.IsActive, request.IsActive ?? v.IsActive)
                { BeforeOldPrice = v.OldPrice, AfterOldPrice = v.OldPrice > after ? v.OldPrice : null };
        }).ToList();
        if (rows.Any(r => r.AfterPrice is < .01m or > 999999999.99m)) throw new ArgumentException("Получившаяся цена выходит за допустимые пределы.");
        var expires = clock.GetUtcNow().AddMinutes(10);
        var plan = new BulkPlan(actorId, sellerUserId, expires, products.ToDictionary(p => p.Id, p => p.UpdatedAt), rows);
        return new(protector.Protect(JsonSerializer.Serialize(plan)), expires, rows);
    }

    public async Task<int> ApplyAsync(Guid actorId, Guid? sellerUserId, string token, CancellationToken ct)
    {
        BulkPlan plan;
        try { plan = JsonSerializer.Deserialize<BulkPlan>(protector.Unprotect(token)) ?? throw new JsonException(); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { throw new BusinessRuleException("Предпросмотр недействителен. Повторите подготовку изменений."); }
        if (plan.ActorId != actorId || plan.SellerUserId != sellerUserId || plan.ExpiresAt <= clock.GetUtcNow()) throw new BusinessRuleException("Предпросмотр истёк или принадлежит другому аккаунту.");
        var products = await Products(plan.Versions.Keys.ToList(), sellerUserId, ct);
        var ids = products.Select(p => p.Id).ToArray();
        var variants = await db.ProductVariants.Where(v => ids.Contains(v.ProductId)).ToListAsync(ct);
        if (products.Any(p => p.UpdatedAt != plan.Versions[p.Id]) || variants.Count != plan.Rows.Count
            || plan.Rows.Any(r => !variants.Any(v => v.Id == r.VariantId && v.ProductId == r.ProductId && v.Price == r.BeforePrice && v.OldPrice == r.BeforeOldPrice && v.StockQuantity == r.BeforeStock && v.IsActive == r.BeforeActive)))
            throw new BusinessRuleException("Товары изменились после предпросмотра. Сформируйте новый предпросмотр.");
        foreach (var row in plan.Rows)
        {
            var v = variants.Single(v => v.Id == row.VariantId);
            v.Update(v.Sku, v.Barcode, v.Name, row.AfterPrice, row.AfterOldPrice, v.CostPrice, v.Weight, v.Length, v.Width, v.Height, row.AfterActive);
            v.SetStock(row.AfterStock);
        }
        foreach (var product in products)
        {
            var next = clock.GetUtcNow().UtcDateTime;
            product.UpdatedAt = next > product.UpdatedAt ? next : product.UpdatedAt.AddTicks(10);
        }
        await db.SaveChangesAsync(ct);
        return products.Count;
    }

    private async Task<List<Domain.Entities.Product.Product>> Products(IReadOnlyList<Guid> ids, Guid? sellerUserId, CancellationToken ct)
    {
        var query = db.Products.Where(p => ids.Contains(p.Id));
        if (sellerUserId.HasValue)
        {
            query = query.Where(p => db.Stores.Any(s => s.Id == p.StoreId && s.IsActive && db.Sellers.Any(v => v.Id == s.SellerId && v.UserId == sellerUserId && v.Status == SellerStatus.ACTIVE)));
        }
        var products = await query.ToListAsync(ct);
        if (products.Count != ids.Count) throw new KeyNotFoundException("Часть выбранных товаров недоступна.");
        if (products.Any(p => p.Status is ProductStatus.PENDING_MODERATION or ProductStatus.ARCHIVED)) throw new BusinessRuleException("Товары на модерации или в архиве нельзя менять массово.");
        if (sellerUserId.HasValue)
        {
            var allowed = await categories.AllowedIdsAsync(sellerUserId.Value, ct);
            if (products.Any(p => !allowed.Contains(p.CategoryId))) throw new BusinessRuleException("Нет доступа к категории выбранного товара.");
        }
        return products;
    }
    public sealed record BulkPlan(Guid ActorId, Guid? SellerUserId, DateTimeOffset ExpiresAt, Dictionary<Guid, DateTime> Versions, List<BulkCatalogRowDto> Rows);
}
