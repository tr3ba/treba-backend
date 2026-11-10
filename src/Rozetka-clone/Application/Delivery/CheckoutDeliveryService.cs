using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Application.Common;
using Application.Promotions;
using Contracts.Delivery;
using Domain.Entities.Orders;
using Domain.Entities.Product;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Delivery;

public sealed class CheckoutDeliveryService(
    IApplicationDbContext db,
    ICheckoutDeliveryProvider provider,
    IDiscountService discounts,
    ICurrentActor actor,
    TimeProvider clock)
{
    public DeliveryCapabilities Capabilities() => new(provider.Available);

    public async Task<DeliveryQuoteView> QuoteAsync(DeliveryQuoteRequest request, CancellationToken ct)
    {
        var userId = actor.UserId ?? throw new UnauthorizedAccessException();

        if (!provider.Available)
            throw new DeliveryUnavailableException();

        ValidateQuoteRequest(request);

        var ids = request.Items.Select(i => i.VariantId).ToArray();

        var variants = await FindAvailableVariantsAsync(ids, ct);

        if (variants.Count != ids.Length)
            throw new BusinessRuleException("Товари змінилися або недоступні. Оновіть кошик.");

        var calculation = CalculateItems(request.Items, variants);

        if (calculation.WeightKg is <= 0 or > 1000)
            throw new ArgumentException("Для цієї ваги потрібне окреме погодження доставки.");

        var discount = await discounts.CalculateDiscountAsync(userId, calculation.Prices, request.CouponCode, ct);

        if (discount.TotalAmount > 100000000)
            throw new ArgumentException("The order amount is too large.");

        var result = await provider.QuoteAsync(request.CityRef, request.PickupPointRef, calculation.WeightKg,
            Math.Max(1, discount.TotalAmount), ct);

        var quote = new DeliveryQuote
        {
            UserId = userId,
            Fingerprint = Fingerprint(request.Items, request.CouponCode),
            CityRef = result.City.Ref,
            PickupPointRef = result.PickupPoint.Ref,
            City = result.City.Name,
            PickupPoint = result.PickupPoint.Name,
            Postomat = result.PickupPoint.Postomat,
            FreeDelivery = discount.FreeDelivery,
            Price = discount.FreeDelivery ? 0 : result.Price,
            Subtotal = discount.Subtotal,
            DeclaredValue = discount.TotalAmount,
            WeightKg = calculation.WeightKg,
            EstimatedWeight = calculation.EstimatedWeight,
            ExpiresAt = clock.GetUtcNow().AddMinutes(10)
        };

        db.DeliveryQuotes.Add(quote);
        await db.SaveChangesAsync(ct);

        return new(quote.Id, quote.Price, "UAH", quote.City, quote.PickupPoint, calculation.WeightKg,
            calculation.EstimatedWeight, quote.ExpiresAt);
    }

    private static void ValidateQuoteRequest(DeliveryQuoteRequest request)
    {
        if (request.Items is null)
            throw new ArgumentException("Перевірте товари та відділення доставки.");

        var hasInvalidDestination = request.CityRef == Guid.Empty || request.PickupPointRef == Guid.Empty;

        var hasInvalidItems = request.Items.Count is < 1 or > 100
            || request.Items.Any(item => item is null || item.Quantity is < 1 or > 1000000);

        var hasDuplicateVariants = request.Items
            .Select(item => item.VariantId)
            .Distinct()
            .Count() != request.Items.Count;

        var hasLongCouponCode = request.CouponCode?.Length > 100;

        if (hasInvalidDestination || hasInvalidItems || hasDuplicateVariants || hasLongCouponCode)
            throw new ArgumentException("Перевірте товари та відділення доставки.");
    }

    private async Task<List<ProductVariant>> FindAvailableVariantsAsync(Guid[] ids, CancellationToken ct)
    {
        return await (from variant in db.ProductVariants.AsNoTracking()
                      join product in db.Products on variant.ProductId equals product.Id
                      join store in db.Stores on product.StoreId equals store.Id
                      join seller in db.Sellers on store.SellerId equals seller.Id
                      where ids.Contains(variant.Id)
                          && variant.IsActive
                          && variant.Price > 0
                          && product.Status == ProductStatus.ACTIVE
                          && store.IsActive
                          && seller.Status == SellerStatus.ACTIVE
                          && db.Categories.Any(category => category.Id == product.CategoryId && category.IsActive)
                          && db.Brands.Any(brand => brand.Id == product.BrandId && brand.IsActive)
                      select variant).ToListAsync(ct);
    }

    private (decimal WeightKg, bool EstimatedWeight, List<PriceItem> Prices) CalculateItems(
        IReadOnlyCollection<DeliveryQuoteItem> items,
        IReadOnlyCollection<ProductVariant> variants)
    {
        var variantsById = variants.ToDictionary(variant => variant.Id);
        decimal totalWeightKg = 0;
        var estimatedWeight = false;
        var prices = new List<PriceItem>(items.Count);

        foreach (var item in items)
        {
            var variant = variantsById[item.VariantId];

            if (variant.StockQuantity < item.Quantity)
                throw new BusinessRuleException("Недостатньо товару на складі.");

            var (unitWeightKg, isEstimated) = ResolveWeight(variant.Weight);

            totalWeightKg += unitWeightKg * item.Quantity;
            estimatedWeight |= isEstimated;
            prices.Add(new PriceItem(variant.Id, item.Quantity));
        }

        var roundedWeightKg = decimal.Round(totalWeightKg, 3, MidpointRounding.AwayFromZero);
        return (roundedWeightKg, estimatedWeight, prices);
    }

    private (decimal WeightKg, bool Estimated) ResolveWeight(double? weightKg)
    {
        var hasKnownWeight = weightKg.HasValue
            && double.IsFinite(weightKg.Value)
            && weightKg.Value > 0
            && weightKg.Value <= 1000;

        return hasKnownWeight
            ? ((decimal)weightKg!.Value, false)
            : (provider.DefaultWeightKg, true);
    }

    public async Task<DeliveryQuote> ValidateAsync(
        Guid id,
        Guid userId,
        IEnumerable<DeliveryQuoteItem> items,
        string? coupon,
        decimal subtotal,
        decimal total,
        CancellationToken ct)
    {
        var quote = await db.DeliveryQuotes.SingleOrDefaultAsync(q => q.Id == id && q.UserId == userId, ct)
            ?? throw new KeyNotFoundException();

        if (quote.UsedByOrderId.HasValue
            || quote.ExpiresAt <= clock.GetUtcNow()
            || quote.Fingerprint != Fingerprint(items, coupon)
            || quote.Subtotal != subtotal
            || quote.DeclaredValue != total)
            throw new BusinessRuleException("Розрахунок доставки застарів. Розрахуйте доставку ще раз.");

        var currentDiscount = await discounts.CalculateDiscountAsync(userId,
            items.Select(i => new PriceItem(i.VariantId, i.Quantity)).ToList(), coupon, ct);
        if (currentDiscount.FreeDelivery != quote.FreeDelivery)
            throw new BusinessRuleException("Delivery discount changed. Request a new delivery quote.");

        var quantities = items.ToDictionary(i => i.VariantId, i => i.Quantity);

        var weights = await db.ProductVariants.AsNoTracking().Where(v => quantities.Keys.Contains(v.Id))
            .Select(v => new { v.Id, v.Weight }).ToListAsync(ct);

        decimal currentWeight = 0;
        var estimated = false;
        foreach (var variant in weights)
        {
            var (unitWeightKg, isEstimated) = ResolveWeight(variant.Weight);

            estimated |= isEstimated;
            currentWeight += unitWeightKg * quantities[variant.Id];
        }

        var roundedWeightKg = decimal.Round(currentWeight, 3, MidpointRounding.AwayFromZero);

        if (weights.Count != quantities.Count
            || roundedWeightKg != quote.WeightKg
            || estimated != quote.EstimatedWeight)
            throw new BusinessRuleException("Вага товарів змінилася. Розрахуйте доставку ще раз.");

        return quote;
    }

    public static string Fingerprint(IEnumerable<DeliveryQuoteItem> items, string? coupon)
    {
        var itemFingerprints = items.OrderBy(item => item.VariantId)
            .Select(item => $"{item.VariantId:N}:{item.Quantity}");

        var payload = string.Join(";", itemFingerprints) + "|" + coupon?.Trim();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static OrderDeliveryView View(OrderShipment shipment, string status) => new(shipment.Carrier, "Managed",
        shipment.Reference, shipment.City, shipment.PickupPoint, shipment.Postomat, shipment.QuotedPrice,
        shipment.WeightKg, shipment.EstimatedWeight, status, "");
}
