using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts.Delivery
{
    public sealed record CreateShipmentResponse(
        string TrackingNumber,
        decimal Price,
        DateTimeOffset? EstimatedDeliveryDate
    );

}
