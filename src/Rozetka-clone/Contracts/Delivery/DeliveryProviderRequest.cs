using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts.Delivery
{
    public sealed record DeliveryProviderRequest(
        string City,
        decimal OrderAmount,
        int TotalQuantity
    );
}
