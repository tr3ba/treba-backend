using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts.Delivery
{
    public sealed record CreateShipmentRequest(
        Guid OrderId,
        Guid DeliveryMethodId,
        string City,
        string Address,
        decimal OrderAmount,
        int TotalQuantity
    );

}
