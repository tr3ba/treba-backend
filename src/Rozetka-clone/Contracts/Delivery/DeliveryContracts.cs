using System.ComponentModel.DataAnnotations;

namespace Contracts.Delivery;

public sealed record DeliveryCapabilities(bool Available, string ShipmentMode = "Managed");
public sealed record DeliveryCity(Guid Ref, string Name, string Area);
public sealed record DeliveryPickupPoint(Guid Ref, Guid CityRef, string Name, string Number, bool Postomat);
public sealed record DeliveryPickupPage(IReadOnlyList<DeliveryPickupPoint> Items, int Page, bool HasMore);
public sealed record DeliveryQuoteItem(Guid VariantId, [Range(1, 1000000)] int Quantity);
public sealed record DeliveryQuoteRequest(Guid CityRef, Guid PickupPointRef,
    [MinLength(1), MaxLength(100)] List<DeliveryQuoteItem> Items, [StringLength(100)] string? CouponCode = null);
public sealed record DeliveryQuoteView(Guid Id, decimal Price, string Currency, string City, string PickupPoint,
    decimal WeightKg, bool EstimatedWeight, DateTimeOffset ExpiresAt, string ShipmentMode = "Managed");
public sealed record OrderDeliveryView(string Carrier, string Mode, string? Reference, string City,
    string PickupPoint, bool Postomat, decimal QuotedPrice, decimal WeightKg, bool EstimatedWeight,
    string Status, string Notice);
