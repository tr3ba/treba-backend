using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts.Delivery
{
    public sealed record DeliveryCalculationRequest(
        string City,
        decimal OrderAmount,
        int TotalQuantity
    );

}
