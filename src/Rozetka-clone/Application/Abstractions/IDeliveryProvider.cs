using Contracts.Delivery;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Abstractions
{
    public interface IDeliveryProvider
    {
        Task<List<PickupPointDto>> GetPickupPointsAsync(string city, CancellationToken cancellationToken = default);

        Task<DeliveryPriceDto> CalculatePriceAsync(DeliveryProviderRequest request, CancellationToken cancellationToken = default);

        Task<CreateShipmentResponse> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken cancellationToken = default);

        Task<TrackingDto> TrackAsync(string trackingNumber, CancellationToken cancellationToken = default);
    }

}
