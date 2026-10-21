using Application.Abstractions;
using Contracts.Delivery;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Delivery
{
    public sealed class MockDeliveryProvider : IDeliveryProvider
    {
        public Task<List<PickupPointDto>> GetPickupPointsAsync(
            string city,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(new List<PickupPointDto>());
        }

        public Task<DeliveryPriceDto> CalculatePriceAsync(
            DeliveryProviderRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var price = 50m;

            var estimatedDeliveryDate = DateTimeOffset.UtcNow.AddDays(3);

            return Task.FromResult(
                new DeliveryPriceDto(
                    price,
                    estimatedDeliveryDate
                )
            );
        }

        public Task<CreateShipmentResponse> CreateShipmentAsync(
            CreateShipmentRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var trackingNumber = $"MOCK-{Guid.NewGuid():N}";

            var price = 50m;

            var estimatedDeliveryDate = DateTimeOffset.UtcNow.AddDays(3);

            return Task.FromResult(
                new CreateShipmentResponse(
                    trackingNumber,
                    price,
                    estimatedDeliveryDate
                )
            );
        }

        public Task<TrackingDto> TrackAsync(
            string trackingNumber,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                new TrackingDto(
                    trackingNumber,
                    "IN_TRANSIT",
                    DateTimeOffset.UtcNow.AddDays(2),
                    DateTimeOffset.UtcNow.AddDays(-1),
                    null
                )
            );
        }
    }
}
