using System;
using System.Collections.Generic;
using System.Text;

namespace Contracts.Delivery
{
    public sealed record DeliveryOptionDto(
        Guid DeliveryMethodId,
        string Name,
        string Code,
        string? Description,
        decimal Price,
        string Provider
    );

}
