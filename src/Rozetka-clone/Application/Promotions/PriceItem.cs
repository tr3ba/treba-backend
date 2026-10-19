using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Promotions
{
    public sealed record PriceItem(
        Guid VariantId,
        int Quantity
    );
}
