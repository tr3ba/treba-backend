using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Application.Common;
using Application.Promotions;
using Contracts.Delivery;
using Domain.Entities.Orders;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Delivery;

public sealed class DeliveryService(IApplicationDbContext db, IDeliveryProvider provider,
    IDiscountService discounts, ICurrentActor actor, TimeProvider clock)
{
    public DeliveryCapabilities Capabilities() => new(provider.Available);

    public async Task<DeliveryQuoteView> QuoteAsync(DeliveryQuoteRequest request, CancellationToken ct)
    {
        var userId = actor.UserId ?? throw new UnauthorizedAccessException();
        if (!provider.Available) throw new DeliveryUnavailableException();
        if (request.CityRef == Guid.Empty || request.PickupPointRef == Guid.Empty || request.Items is null
            || request.Items.Count is < 1 or > 100 || request.Items.Any(i => i is null || i.Quantity is < 1 or > 1000000)
            || request.Items.Select(i => i.VariantId).Distinct().Count() != request.Items.Count || request.CouponCode?.Length > 100)
            throw new ArgumentException("Перевірте товари та відділення доставки.");
        var ids = request.Items.Select(i => i.VariantId).ToArray();
        var rows = await (from v in db.ProductVariants.AsNoTracking()
                          join p in db.Products on v.ProductId equals p.Id
                          join store in db.Stores on p.StoreId equals store.Id
                          join seller in db.Sellers on store.SellerId equals seller.Id
                          where ids.Contains(v.Id) && v.IsActive && v.Price > 0 && p.Status == ProductStatus.ACTIVE
                              && store.IsActive && seller.Status == SellerStatus.ACTIVE
                              && db.Categories.Any(c => c.Id == p.CategoryId && c.IsActive)
                              && db.Brands.Any(b => b.Id == p.BrandId && b.IsActive)
                          select new { Variant = v, Product = p, SellerId = seller.Id }).ToListAsync(ct);
        if (rows.Count != ids.Length) throw new BusinessRuleException("Товари змінилися або недоступні. Оновіть кошик.");
        decimal weight = 0;
        var estimated = false;
        var prices = new List<PriceItem>();
        foreach (var item in request.Items)
        {
            var row = rows.Single(r => r.Variant.Id == item.VariantId);
            if (row.Variant.StockQuantity < item.Quantity) throw new BusinessRuleException("Недостатньо товару на складі.");
            var kg = row.Variant.Weight;
            var knownWeight = kg.HasValue && double.IsFinite(kg.Value) && kg.Value > 0 && kg.Value <= 1000;
            estimated |= !knownWeight;
            weight += (knownWeight ? (decimal)kg!.Value : provider.DefaultWeightKg) * item.Quantity;
            prices.Add(new PriceItem(row.Variant.Id, item.Quantity));
        }
        weight = decimal.Round(weight, 3, MidpointRounding.AwayFromZero);
        if (weight is <= 0 or > 1000) throw new ArgumentException("Для цієї ваги потрібне окреме погодження доставки.");
        var discount = await discounts.CalculateDiscountAsync(userId, prices, request.CouponCode, ct);
        if (discount.TotalAmount > 100000000) throw new ArgumentException("The order amount is too large.");
        var result = await provider.QuoteAsync(request.CityRef, request.PickupPointRef, weight,
            Math.Max(1, discount.TotalAmount), ct);
        var quote = new DeliveryQuote
        {
            UserId = userId, Fingerprint = Fingerprint(request.Items, request.CouponCode),
            CityRef = result.City.Ref, PickupPointRef = result.PickupPoint.Ref, City = result.City.Name,
            PickupPoint = result.PickupPoint.Name, Postomat = result.PickupPoint.Postomat,
            Price = discount.FreeDelivery ? 0 : result.Price, Subtotal = discount.Subtotal,
            DeclaredValue = discount.TotalAmount, WeightKg = weight, EstimatedWeight = estimated,
            ExpiresAt = clock.GetUtcNow().AddMinutes(10)
        };
        db.DeliveryQuotes.Add(quote); await db.SaveChangesAsync(ct);
        return new(quote.Id, quote.Price, "UAH", quote.City, quote.PickupPoint, weight, estimated, quote.ExpiresAt);
    }

    public async Task<DeliveryQuote> ValidateAsync(Guid id, Guid userId, IEnumerable<DeliveryQuoteItem> items,
        string? coupon, decimal subtotal, decimal total, CancellationToken ct)
    {
        var quote = await db.DeliveryQuotes.SingleOrDefaultAsync(q => q.Id == id && q.UserId == userId, ct)
            ?? throw new KeyNotFoundException();
        if (quote.UsedByOrderId.HasValue || quote.ExpiresAt <= clock.GetUtcNow()
            || quote.Fingerprint != Fingerprint(items, coupon) || quote.Subtotal != subtotal || quote.DeclaredValue != total)
            throw new BusinessRuleException("Розрахунок доставки застарів. Розрахуйте доставку ще раз.");
        var quantities = items.ToDictionary(i => i.VariantId, i => i.Quantity);
        var weights = await db.ProductVariants.AsNoTracking().Where(v => quantities.Keys.Contains(v.Id))
            .Select(v => new { v.Id, v.Weight }).ToListAsync(ct);
        decimal currentWeight = 0; var estimated = false;
        foreach (var variant in weights)
        {
            var known = variant.Weight.HasValue && double.IsFinite(variant.Weight.Value) && variant.Weight.Value > 0 && variant.Weight.Value <= 1000;
            estimated |= !known;
            currentWeight += (known ? (decimal)variant.Weight!.Value : provider.DefaultWeightKg) * quantities[variant.Id];
        }
        if (weights.Count != quantities.Count || decimal.Round(currentWeight, 3, MidpointRounding.AwayFromZero) != quote.WeightKg || estimated != quote.EstimatedWeight)
            throw new BusinessRuleException("Вага товарів змінилася. Розрахуйте доставку ще раз.");
        return quote;
    }

    public static string Fingerprint(IEnumerable<DeliveryQuoteItem> items, string? coupon) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join(";", items.OrderBy(i => i.VariantId).Select(i => $"{i.VariantId:N}:{i.Quantity}")) + "|" + coupon?.Trim())));

    public static OrderDeliveryView View(OrderShipment shipment, string status) => new(shipment.Carrier, "Managed",
        shipment.Reference, shipment.City, shipment.PickupPoint, shipment.Postomat, shipment.QuotedPrice,
        shipment.WeightKg, shipment.EstimatedWeight, status, "");
}
