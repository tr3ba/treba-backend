using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts.Delivery
{
    public sealed record ShipmentDto(
        Guid Id,
        Guid OrderId,
        Guid DeliveryMethodId,
        string Provider,
        string? TrackingNumber,
        string Status,
        decimal Price,
        DateTimeOffset? EstimatedDeliveryDate,
        DateTimeOffset? ShippedAt,
        DateTimeOffset? DeliveredAt
    );

}
