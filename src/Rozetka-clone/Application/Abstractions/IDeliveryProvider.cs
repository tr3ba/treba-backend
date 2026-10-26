using Contracts.Delivery;

namespace Application.Abstractions;

public interface IDeliveryProvider
{
    bool Available { get; }
    decimal DefaultWeightKg { get; }
    Task<IReadOnlyList<DeliveryCity>> CitiesAsync(string query, CancellationToken ct);
    Task<DeliveryPickupPage> PickupPointsAsync(Guid cityRef, string query, string kind, int page, CancellationToken ct);
    Task<(DeliveryCity City, DeliveryPickupPoint PickupPoint, decimal Price)> QuoteAsync(Guid cityRef,
        Guid pickupPointRef, decimal weightKg, decimal declaredValue, CancellationToken ct);
}

public sealed class DeliveryUnavailableException : Exception
{
    public DeliveryUnavailableException() : base("Доставка Новою поштою тимчасово недоступна. Спробуйте пізніше.") { }
}

