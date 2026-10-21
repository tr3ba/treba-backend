using Application.Abstractions;
using Application.Common;
using Domain.Entities.Inventory;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Inventory;

// Catalog stock is available-to-sell. Reserve decrements it; cancel restores it once.
public sealed class InventoryService(IApplicationDbContext db, ICurrentActor actor) : IInventoryService
{
    public async Task<InventoryDto?> GetInventoryAsync(Guid variantId, CancellationToken ct = default)
    {
        var variant = await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == variantId, ct);
        if (variant is null)
            return null;
        var reserved = await db.InventoryReservations.Where(r => r.VariantId == variantId && r.Status == ReservationStatus.ACTIVE)
            .SumAsync(r => (int?)r.Quantity, ct) ?? 0;
        return new(variantId, Guid.Empty, variantId, variant.StockQuantity + reserved, reserved, variant.StockQuantity, 0);
    }
    public Task<bool> IsAvailableAsync(Guid variantId, int quantity, CancellationToken ct = default) =>
        quantity <= 0 ? Task.FromResult(false) : db.ProductVariants.AnyAsync(v => v.Id == variantId && v.IsActive && v.StockQuantity >= quantity, ct);
    public Task IncreaseStockAsync(Guid variantId, Guid warehouseId, int quantity, CancellationToken ct = default) => Adjust(variantId, warehouseId, quantity, true, ct);
    public Task DecreaseStockAsync(Guid variantId, Guid warehouseId, int quantity, CancellationToken ct = default) => Adjust(variantId, warehouseId, quantity, false, ct);
    private async Task Adjust(Guid variantId, Guid warehouseId, int quantity, bool increase, CancellationToken ct)
    {
        if (warehouseId != Guid.Empty)
            throw new ArgumentException("Catalog stock is managed without warehouses; use an empty warehouse id.");
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive.");
        var variant = await db.ProductVariants.SingleOrDefaultAsync(v => v.Id == variantId, ct) ?? throw new KeyNotFoundException();
        if (actor.Role == "Seller" && !await db.Products.AnyAsync(p => p.Id == variant.ProductId
            && db.Stores.Any(s => s.Id == p.StoreId && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == actor.UserId && seller.Status == SellerStatus.ACTIVE)), ct))
            throw new KeyNotFoundException();
        if (!increase && variant.StockQuantity < quantity)
            throw new BusinessRuleException("Insufficient available stock.");
        variant.SetStock(checked(variant.StockQuantity + (increase ? quantity : -quantity)));
        await TouchProducts([variant.ProductId], ct);
        await db.SaveChangesAsync(ct);
    }
    internal async Task<List<InventoryReservation>> StageReserveAsync(Guid orderId, List<OrderItemRequest> items, CancellationToken ct)
    {
        if (items.Count == 0 || items.Any(i => i.Quantity <= 0) || items.Select(i => i.VariantId).Distinct().Count() != items.Count)
            throw new ArgumentException("Order quantities must be positive and variants unique.");
        var ids = items.Select(i => i.VariantId).ToList();
        var variants = await db.ProductVariants.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var item in items)
            if (!variants.TryGetValue(item.VariantId, out var v) || !v.IsActive || v.StockQuantity < item.Quantity)
                throw new BusinessRuleException("Insufficient available stock. Refresh the order.");
        var reservations = items.Select(i => new InventoryReservation
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            VariantId = i.VariantId,
            WarehouseId = Guid.Empty,
            Quantity = i.Quantity,
            Status = ReservationStatus.ACTIVE,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        }).ToList();
        foreach (var item in items)
            variants[item.VariantId].SetStock(variants[item.VariantId].StockQuantity - item.Quantity);
        await TouchProducts(variants.Values.Select(v => v.ProductId), ct);
        db.InventoryReservations.AddRange(reservations);
        return reservations;
    }
    internal async Task StageConfirmAsync(Guid orderId, CancellationToken ct)
    {
        var reservations = await db.InventoryReservations.Where(r => r.OrderId == orderId && r.Status == ReservationStatus.ACTIVE).ToListAsync(ct);
        await ValidateReservations(orderId, reservations, ct);
        if (reservations.Any(r => r.ExpiresAt <= DateTimeOffset.UtcNow))
            throw new BusinessRuleException("The stock reservation has expired. Cancel this order and place a new one.");
        foreach (var r in reservations)
        {
            if (r.WarehouseId != Guid.Empty)
                throw new BusinessRuleException("Reconcile this legacy warehouse reservation with catalog stock before processing the order.");
            r.Status = ReservationStatus.CONFIRMED;
        }
    }
    internal async Task StageRestoreAsync(Guid orderId, CancellationToken ct)
    {
        var reservations = await db.InventoryReservations.Where(r => r.OrderId == orderId
            && (r.Status == ReservationStatus.ACTIVE || r.Status == ReservationStatus.CONFIRMED)).ToListAsync(ct);
        await ValidateReservations(orderId, reservations, ct);
        var ids = reservations.Select(r => r.VariantId).ToList();
        var variants = await db.ProductVariants.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var r in reservations)
        {
            if (!variants.TryGetValue(r.VariantId, out var variant))
                throw new BusinessRuleException("A reserved variant is missing; stock restoration requires investigation.");
            if (r.WarehouseId != Guid.Empty)
                throw new BusinessRuleException("Reconcile this legacy warehouse reservation with catalog stock before processing the order.");
            variant.SetStock(checked(variant.StockQuantity + r.Quantity));
            r.Status = ReservationStatus.RELEASED;
        }
        await TouchProducts(variants.Values.Select(v => v.ProductId), ct);
    }
    private async Task TouchProducts(IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        foreach (var product in await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct))
            product.UpdatedAt = DateTime.UtcNow;
    }
    private async Task ValidateReservations(Guid orderId, List<InventoryReservation> reservations, CancellationToken ct)
    {
        var items = await db.OrderItems.Where(i => i.OrderId == orderId).Select(i => new { i.VariantId, i.Quantity }).ToListAsync(ct);
        if (items.Count == 0 || reservations.Any(r => r.WarehouseId != Guid.Empty)
            || items.GroupBy(i => i.VariantId).Any(g => reservations.Where(r => r.VariantId == g.Key).Sum(r => r.Quantity) != g.Sum(i => i.Quantity))
            || reservations.Any(r => !items.Any(i => i.VariantId == r.VariantId)))
            throw new BusinessRuleException("The order reservations require stock reconciliation before this operation.");
    }
    // Standalone reservation writes would bypass order totals, ownership and history.
    public Task<List<InventoryReservation>> ReserveItemsAsync(Guid orderId, List<OrderItemRequest> items, CancellationToken ct = default) =>
        throw new BusinessRuleException("Create an order to reserve stock.");
    public Task ConfirmReservationAsync(Guid orderId, CancellationToken ct = default) =>
        throw new BusinessRuleException("Confirm payment through the order workflow.");
    public Task ReleaseReservationAsync(Guid orderId, CancellationToken ct = default) =>
        throw new BusinessRuleException("Cancel the order to release its stock.");
}
