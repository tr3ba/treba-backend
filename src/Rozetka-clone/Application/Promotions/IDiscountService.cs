using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Promotions
{
    public interface IDiscountService
    {
        Task<DiscountResult> CalculateDiscountAsync(
            Guid userId,
            IReadOnlyCollection<PriceItem> items,
            string? couponCode,
            CancellationToken cancellationToken = default
        );

        Task<CouponValidationResult> ValidateCouponAsync(
            Guid userId,
            string couponCode,
            CancellationToken cancellationToken = default
        );

        Task RegisterCouponUsageAsync(
            Guid couponId,
            Guid userId,
            Guid orderId,
            CancellationToken cancellationToken = default
        );
    }
}
