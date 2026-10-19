using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Promotions
{
    public sealed record CouponValidationResult(
        bool IsValid,
        string? ErrorCode,
        string? ErrorMessage,
        Guid? CouponId,
        Guid? PromotionId
    );
}
