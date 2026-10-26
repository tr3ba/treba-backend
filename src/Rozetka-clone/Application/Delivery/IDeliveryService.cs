using Contracts.Delivery;
using Domain.Enums;


namespace Application.Delivery
{
    public interface IDeliveryService
    {
        Task<List<DeliveryOptionDto>> CalculateOptionsAsync(DeliveryCalculationRequest request, CancellationToken cancellationToken = default);

        Task<List<PickupPointDto>> GetPickupPointsAsync(string city, CancellationToken cancellationToken = default);

        Task<ShipmentDto> CreateShipmentAsync(Guid orderId, Guid deliveryMethodId, CancellationToken cancellationToken = default);

        Task<ShipmentDto> UpdateStatusAsync(Guid shipmentId, ShipmentStatus status, CancellationToken cancellationToken = default);

        Task<TrackingDto> TrackShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    }

}
