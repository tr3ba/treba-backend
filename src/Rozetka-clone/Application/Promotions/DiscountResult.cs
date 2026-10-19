using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Promotions
{
    public sealed record DiscountResult(
        decimal Subtotal,
        decimal DiscountAmount,
        decimal TotalAmount,
        bool FreeDelivery,
        Guid? CouponId,
        Guid? PromotionId
    );
}
