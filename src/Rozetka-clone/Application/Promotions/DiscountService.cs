using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Application.Promotions
{
    public sealed class DiscountService : IDiscountService
    {
        private readonly IApplicationDbContext _dbContext;

        public DiscountService(IApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<DiscountResult> CalculateDiscountAsync(
            Guid userId,
            IReadOnlyCollection<PriceItem> items,
            string? couponCode,
            CancellationToken cancellationToken = default
        )
        {
            if (items.Count == 0)
            {
                return new DiscountResult(
                    0m,
                    0m,
                    0m,
                    false,
                    null,
                    null
                );
            }

            if (items.Any(x => x.Quantity <= 0))
            {
                throw new ArgumentException(
                    "Item quantity must be greater than zero.",
                    nameof(items)
                );
            }

            var variantIds = items
                .Select(x => x.VariantId)
                .Distinct()
                .ToList();

            var productData = await (
                from variant in _dbContext.ProductVariants.AsNoTracking()
                join product in _dbContext.Products.AsNoTracking()
                    on variant.ProductId equals product.Id
                join store in _dbContext.Stores.AsNoTracking()
                    on product.StoreId equals store.Id
                where
                    variantIds.Contains(variant.Id)
                    && variant.IsActive
                select new
                {
                    VariantId = variant.Id,
                    ProductId = product.Id,
                    product.CategoryId,
                    product.BrandId,
                    store.SellerId,
                    variant.Price
                }
            ).ToListAsync(cancellationToken);

            if (productData.Count != variantIds.Count)
            {
                throw new KeyNotFoundException(
                    "One or more product variants were not found or are inactive."
                );
            }

            var pricedItems = items
                .Select(item =>
                {
                    var product = productData.Single(
                        x => x.VariantId == item.VariantId
                    );

                    return new
                    {
                        item.VariantId,
                        item.Quantity,
                        product.ProductId,
                        product.CategoryId,
                        product.BrandId,
                        product.SellerId,
                        product.Price,
                        LineTotal = product.Price * item.Quantity
                    };
                })
                .ToList();

            var subtotal = items.Sum(item =>
            {
                var product = productData.Single(
                    x => x.VariantId == item.VariantId
                );

                return product.Price * item.Quantity;
            });

            if (string.IsNullOrWhiteSpace(couponCode))
            {
                return new DiscountResult(
                    subtotal,
                    0m,
                    subtotal,
                    false,
                    null,
                    null
                );
            }

            var couponValidation = await ValidateCouponAsync(
                userId,
                couponCode,
                cancellationToken
            );

            if (!couponValidation.IsValid)
            {
                throw new InvalidOperationException(
                    couponValidation.ErrorMessage ?? "Coupon is invalid."
                );
            }

            var promotionId = couponValidation.PromotionId!.Value;

            var promotion = await _dbContext.Promotions
                .AsNoTracking()
                .FirstAsync(
                    x => x.Id == promotionId,
                    cancellationToken
                );

            var rules = await _dbContext.PromotionRules
                .AsNoTracking()
                .Where(x => x.PromotionId == promotionId)
                .ToListAsync(cancellationToken);

            var action = await _dbContext.PromotionActions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.PromotionId == promotionId,
                    cancellationToken
                );

            if (action is null)
            {
                throw new InvalidOperationException(
                    "Promotion action was not found."
                );
            }


            var eligibleItems = pricedItems.ToList();

            foreach (var rule in rules)
            {
                if (rule.RuleType == PromotionRuleType.USER_GROUP)
                {
                    throw new NotSupportedException(
                        "USER_GROUP promotion rules are not supported because user groups are not implemented."
                    );
                }

                if (rule.RuleType == PromotionRuleType.MIN_ORDER_AMOUNT)
                {
                    if (!decimal.TryParse(
                            rule.RuleValue,
                            NumberStyles.Number,
                            CultureInfo.InvariantCulture,
                            out var minimumOrderAmount
                        ))
                    {
                        throw new InvalidOperationException(
                            "Promotion minimum order amount is invalid."
                        );
                    }

                    if (subtotal < minimumOrderAmount)
                    {
                        return new DiscountResult(
                            subtotal, 0m, subtotal, false, null, null
                        );
                    }

                    continue;
                }

                if (!Guid.TryParse(rule.RuleValue, out var ruleId))
                {
                    throw new InvalidOperationException(
                        $"Promotion rule value for '{rule.RuleType}' is invalid."
                    );
                }

                eligibleItems = rule.RuleType switch
                {
                    PromotionRuleType.PRODUCT => eligibleItems
                        .Where(x => x.ProductId == ruleId)
                        .ToList(),

                    PromotionRuleType.CATEGORY => eligibleItems
                        .Where(x => x.CategoryId == ruleId)
                        .ToList(),

                    PromotionRuleType.BRAND => eligibleItems
                        .Where(x => x.BrandId == ruleId)
                        .ToList(),

                    PromotionRuleType.SELLER => eligibleItems
                        .Where(x => x.SellerId == ruleId)
                        .ToList(),

                    _ => throw new NotSupportedException(
                        $"Promotion rule type '{rule.RuleType}' is not supported."
                    )
                };

                if (eligibleItems.Count == 0)
                {
                    return new DiscountResult(
                        subtotal, 0m, subtotal, false, null, null
                    );
                }
            }

            var eligibleSubtotal = eligibleItems.Sum(x => x.LineTotal);


            var discountType = action.DiscountType.Trim().ToUpperInvariant();

            decimal discountAmount;

            switch (discountType)
            {
                case "PERCENTAGE":
                    if (action.DiscountValue < 0m || action.DiscountValue > 100m)
                    {
                        throw new InvalidOperationException(
                            "Percentage discount must be between 0 and 100."
                        );
                    }

                    discountAmount = eligibleSubtotal * action.DiscountValue / 100m;
                    break;

                case "FIXED_AMOUNT":
                    if (action.DiscountValue < 0m)
                    {
                        throw new InvalidOperationException(
                            "Fixed discount amount cannot be negative."
                        );
                    }

                    discountAmount = Math.Min(
                        action.DiscountValue,
                        eligibleSubtotal
                    );
                    break;

                case "FREE_DELIVERY":
                    return new DiscountResult(
                        subtotal,
                        0m,
                        subtotal,
                        true,
                        couponValidation.CouponId,
                        promotion.Id
                    );

                default:
                    throw new NotSupportedException(
                        $"Promotion discount type '{action.DiscountType}' is not supported."
                    );
            }

            if (action.MaximumDiscount.HasValue)
            {
                if (action.MaximumDiscount.Value < 0m)
                {
                    throw new InvalidOperationException(
                        "Maximum discount cannot be negative."
                    );
                }

                discountAmount = Math.Min(
                    discountAmount,
                    action.MaximumDiscount.Value
                );
            }

            discountAmount = Math.Min(
                Math.Round(
                    discountAmount,
                    2,
                    MidpointRounding.AwayFromZero
                ),
                eligibleSubtotal
            );

            var totalAmount = subtotal - discountAmount;

            return new DiscountResult(
                subtotal,
                discountAmount,
                totalAmount,
                false,
                couponValidation.CouponId,
                promotion.Id
            );
        }

        public async Task<CouponValidationResult> ValidateCouponAsync(
            Guid userId,
            string couponCode,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(couponCode))
            {
                return new CouponValidationResult(
                    false,
                    "COUPON_CODE_REQUIRED",
                    "Coupon code is required.",
                    null,
                    null
                );
            }

            var normalizedCode = couponCode.Trim().ToUpperInvariant();
            var now = DateTime.UtcNow;

            var coupon = await _dbContext.Coupons
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Code.ToUpper() == normalizedCode,
                    cancellationToken
                );

            if (coupon is null)
            {
                return new CouponValidationResult(
                    false,
                    "COUPON_NOT_FOUND",
                    "Coupon not found.",
                    null,
                    null
                );
            }

            if (!coupon.Active)
            {
                return new CouponValidationResult(
                    false,
                    "COUPON_INACTIVE",
                    "Coupon is inactive.",
                    coupon.Id,
                    coupon.PromotionId
                );
            }

            if (now < coupon.StartAt)
            {
                return new CouponValidationResult(
                    false,
                    "COUPON_NOT_STARTED",
                    "Coupon is not active yet.",
                    coupon.Id,
                    coupon.PromotionId
                );
            }

            if (now > coupon.EndAt)
            {
                return new CouponValidationResult(
                    false,
                    "COUPON_EXPIRED",
                    "Coupon has expired.",
                    coupon.Id,
                    coupon.PromotionId
                );
            }

            if (coupon.UsageLimit > 0 && coupon.UsedCount >= coupon.UsageLimit)
            {
                return new CouponValidationResult(
                    false,
                    "COUPON_USAGE_LIMIT_REACHED",
                    "Coupon usage limit has been reached.",
                    coupon.Id,
                    coupon.PromotionId
                );
            }

            if (coupon.UsageLimitPerUser > 0)
            {
                var userUsageCount = await _dbContext.CouponUsages
                    .AsNoTracking()
                    .CountAsync(
                        x =>
                            x.CouponId == coupon.Id
                            && x.UserId == userId,
                        cancellationToken
                    );

                if (userUsageCount >= coupon.UsageLimitPerUser)
                {
                    return new CouponValidationResult(
                        false,
                        "COUPON_USER_LIMIT_REACHED",
                        "Coupon usage limit for this user has been reached.",
                        coupon.Id,
                        coupon.PromotionId
                    );
                }
            }

            var promotion = await _dbContext.Promotions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == coupon.PromotionId,
                    cancellationToken
                );

            if (promotion is null)
            {
                return new CouponValidationResult(
                    false,
                    "PROMOTION_NOT_FOUND",
                    "Promotion not found.",
                    coupon.Id,
                    coupon.PromotionId
                );
            }

            if (promotion.Status != Domain.Enums.PromotionStatus.ACTIVE)
            {
                return new CouponValidationResult(
                    false,
                    "PROMOTION_INACTIVE",
                    "Promotion is inactive.",
                    coupon.Id,
                    promotion.Id
                );
            }

            if (now < promotion.StartAt || now > promotion.EndAt)
            {
                return new CouponValidationResult(
                    false,
                    "PROMOTION_OUTSIDE_ACTIVE_PERIOD",
                    "Promotion is outside its active period.",
                    coupon.Id,
                    promotion.Id
                );
            }

            return new CouponValidationResult(
                true,
                null,
                null,
                coupon.Id,
                promotion.Id
            );
        }

        public async Task RegisterCouponUsageAsync(
    Guid couponId,
    Guid userId,
    Guid orderId,
    CancellationToken cancellationToken = default
)
        {
            if (couponId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Coupon identifier cannot be empty.",
                    nameof(couponId)
                );
            }

            if (userId == Guid.Empty)
            {
                throw new ArgumentException(
                    "User identifier cannot be empty.",
                    nameof(userId)
                );
            }

            if (orderId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Order identifier cannot be empty.",
                    nameof(orderId)
                );
            }

            var coupon = await _dbContext.Coupons
                .FirstOrDefaultAsync(
                    x => x.Id == couponId,
                    cancellationToken
                );

            if (coupon is null)
            {
                throw new KeyNotFoundException("Coupon not found.");
            }

            var usage = new CouponUsage
            {
                Id = Guid.NewGuid(),
                CouponId = couponId,
                UserId = userId,
                OrderId = orderId,
                UsedAt = DateTime.UtcNow
            };

            _dbContext.CouponUsages.Add(usage);

            coupon.UsedCount++;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
