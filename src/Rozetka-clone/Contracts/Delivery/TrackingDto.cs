using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts.Delivery
{
    public sealed record TrackingDto(
        string TrackingNumber,
        string Status,
        DateTimeOffset? EstimatedDeliveryDate,
        DateTimeOffset? ShippedAt,
        DateTimeOffset? DeliveredAt
    );

}
