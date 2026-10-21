using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts.Delivery
{
    public sealed record DeliveryPriceDto(
        decimal Price,
        DateTimeOffset? EstimatedDeliveryDate
    );

}
