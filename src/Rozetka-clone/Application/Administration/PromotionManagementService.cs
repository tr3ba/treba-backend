using System.Globalization;
using System.Text.RegularExpressions;
using Application.Abstractions;
using Application.Common;
using Application.Orders;
using Contracts.Admin.Operations;
using Contracts.Common;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Administration;

public sealed class PromotionManagementService(IApplicationDbContext db)
{
    private const int PageSize = 12;
    private const string CouponCodePattern = @"^[A-Za-z0-9_-]+$";

    public async Task<PagedResponse<PromotionView>> ListAsync(
        int page,
        string? search,
        string? status,
        CancellationToken ct)
    {
        OrderService.ValidatePage(page, PageSize);

        var query = db.Promotions.AsNoTracking();
        query = FilterByStatus(query, status);
        query = FilterBySearch(query, search);

        var count = await query.CountAsync(ct);
        var skip = (page - 1) * PageSize;
        var promotions = await query
            .OrderByDescending(promotion => promotion.StartAt)
            .ThenBy(promotion => promotion.Id)
            .Skip(skip)
            .Take(PageSize)
            .ToListAsync(ct);

        var promotionIds = promotions.Select(promotion => promotion.Id).ToList();

        var coupons = await db.Coupons.AsNoTracking()
            .Where(coupon => promotionIds.Contains(coupon.PromotionId))
            .ToListAsync(ct);

        var actions = await db.PromotionActions.AsNoTracking()
            .Where(action => promotionIds.Contains(action.PromotionId))
            .ToListAsync(ct);

        var rules = await db.PromotionRules.AsNoTracking()
            .Where(rule => promotionIds.Contains(rule.PromotionId))
            .ToListAsync(ct);

        var result = promotions.Select(promotion =>
        {
            var action = actions.FirstOrDefault(item => item.PromotionId == promotion.Id);
            var promotionRules = rules.Where(rule => rule.PromotionId == promotion.Id);
            var promotionCoupons = coupons.Where(coupon => coupon.PromotionId == promotion.Id);

            return View(promotion, action, promotionRules, promotionCoupons);
        }).ToList();

        return new(result, page, PageSize, count, (int)Math.Ceiling(count / (double)PageSize));
    }

    public async Task<PromotionView?> GetAsync(Guid id, CancellationToken ct)
    {
        var promotion = await db.Promotions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, ct);

        if (promotion is null)
            return null;

        var action = await db.PromotionActions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.PromotionId == id, ct);

        var rules = await db.PromotionRules.AsNoTracking()
            .Where(item => item.PromotionId == id)
            .ToListAsync(ct);

        var coupons = await db.Coupons.AsNoTracking()
            .Where(item => item.PromotionId == id)
            .ToListAsync(ct);

        return View(promotion, action, rules, coupons);
    }

    public async Task<PromotionView> SaveAsync(Guid? id, PromotionInput request, CancellationToken ct)
    {
        OrderService.ValidateText(request.Name, 255, "Promotion name");
        ValidateDescription(request.Description);
        ValidatePromotionType(request.Type);

        var status = ParsePromotionStatus(request.Status);
        ValidatePromotionDates(request, status);
        ValidateDiscount(request);

        var couponCodes = ValidateCoupons(request.Coupons);

        await EnsureCouponCodesAvailableAsync(id, couponCodes, ct);
        await EnsureTargetsAvailableAsync(request, ct);

        var promotion = await LoadPromotionAsync(id, ct);
        ValidateExpectedVersion(id, request.ExpectedVersion, promotion);

        var existingCoupons = await LoadExistingCouponsAsync(id, ct);
        ValidateExistingCoupons(request.Coupons, existingCoupons);

        if (!id.HasValue)
            db.Promotions.Add(promotion);

        UpdatePromotion(promotion, request, status);

        await ReplaceRulesAsync(promotion, request, ct);
        await ReplaceActionAsync(promotion, request, ct);
        UpdateCoupons(promotion, request.Coupons, existingCoupons);

        await db.SaveChangesAsync(ct);

        return (await GetAsync(promotion.Id, ct))!;
    }

    private static IQueryable<Promotion> FilterByStatus(IQueryable<Promotion> query, string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return query;

        if (!Enum.TryParse<PromotionStatus>(status, out var parsedStatus)
            || !Enum.IsDefined(parsedStatus)
            || parsedStatus.ToString() != status)
        {
            throw new ArgumentException("Unknown promotion status.");
        }

        return query.Where(promotion => promotion.Status == parsedStatus);
    }

    private IQueryable<Promotion> FilterBySearch(IQueryable<Promotion> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLowerInvariant();

        return query.Where(promotion =>
            promotion.Name.ToLower().Contains(term)
            || db.Coupons.Any(coupon =>
                coupon.PromotionId == promotion.Id
                && coupon.Code.ToLower().Contains(term)));
    }

    private static void ValidateDescription(string? description)
    {
        if (description?.Length > 4000)
            throw new ArgumentException("Description is too long.");
    }

    private static void ValidatePromotionType(string type)
    {
        if (type is not ("PERCENTAGE" or "FIXED_AMOUNT"))
            throw new ArgumentException("Choose a percentage or fixed amount discount.");
    }

    private static PromotionStatus ParsePromotionStatus(string status)
    {
        if (!Enum.TryParse<PromotionStatus>(status, out var parsedStatus)
            || !Enum.IsDefined(parsedStatus)
            || parsedStatus.ToString() != status)
        {
            throw new ArgumentException("Unknown promotion status.");
        }

        return parsedStatus;
    }

    private static void ValidatePromotionDates(PromotionInput request, PromotionStatus status)
    {
        if (request.StartAt >= request.EndAt
            || request.StartAt == default
            || request.EndAt == default)
        {
            throw new ArgumentException("The end must be after the start.");
        }

        if (status == PromotionStatus.ACTIVE
            && (request.StartAt > DateTimeOffset.UtcNow || request.EndAt <= DateTimeOffset.UtcNow))
        {
            throw new ArgumentException("An active promotion must be within its dates.");
        }

        if (status == PromotionStatus.SCHEDULED && request.EndAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("A scheduled promotion must end in the future.");
    }

    private static void ValidateDiscount(PromotionInput request)
    {
        var discountIsOutOfRange = request.DiscountValue <= 0
            || request.DiscountValue > 999999999.99m;

        var percentageExceedsMaximum = request.Type == "PERCENTAGE"
            && request.DiscountValue > 100;

        var discountHasTooManyDecimalPlaces = !Money(request.DiscountValue);

        var maximumDiscountIsInvalid = request.MaximumDiscount is <= 0 or > 999999999.99m
            || request.MaximumDiscount.HasValue && !Money(request.MaximumDiscount.Value);

        var minimumOrderAmountIsInvalid = request.MinimumOrderAmount is < 0 or > 999999999.99m
            || request.MinimumOrderAmount.HasValue && !Money(request.MinimumOrderAmount.Value);

        if (discountIsOutOfRange
            || percentageExceedsMaximum
            || discountHasTooManyDecimalPlaces
            || maximumDiscountIsInvalid
            || minimumOrderAmountIsInvalid)
        {
            throw new ArgumentException("Invalid discount or monetary limit.");
        }
    }

    private static List<string> ValidateCoupons(List<CouponInput>? coupons)
    {
        if (coupons is null
            || coupons.Count is < 1 or > 30
            || coupons.Any(coupon =>
                coupon is null
                || string.IsNullOrWhiteSpace(coupon.Code)
                || coupon.Code.Length > 100
                || !Regex.IsMatch(coupon.Code, CouponCodePattern)
                || coupon.UsageLimit < 0
                || coupon.UsageLimitPerUser < 0))
        {
            throw new ArgumentException("Provide valid coupon codes and nonnegative usage limits.");
        }

        var normalizedCodes = coupons
            .Select(coupon => coupon.Code.ToUpperInvariant())
            .ToList();

        var hasDuplicateCodes = normalizedCodes.Distinct().Count() != normalizedCodes.Count;
        var hasDuplicateCouponIds = coupons
            .Where(coupon => coupon.Id.HasValue)
            .Select(coupon => coupon.Id)
            .Distinct()
            .Count() != coupons.Count(coupon => coupon.Id.HasValue);

        if (hasDuplicateCodes || hasDuplicateCouponIds)
            throw new ArgumentException("Duplicate coupons.");

        return normalizedCodes;
    }

    private async Task EnsureCouponCodesAvailableAsync(
        Guid? promotionId,
        List<string> couponCodes,
        CancellationToken ct)
    {
        var codeIsUsedByAnotherPromotion = await db.Coupons.AnyAsync(coupon =>
            couponCodes.Contains(coupon.Code.ToUpper())
            && coupon.PromotionId != promotionId, ct);

        if (codeIsUsedByAnotherPromotion)
            throw new BusinessRuleException("A coupon code is already in use.");
    }

    private async Task EnsureTargetsAvailableAsync(PromotionInput request, CancellationToken ct)
    {
        if (request.ProductId.HasValue
            && !await db.Products.AnyAsync(product => product.Id == request.ProductId, ct))
        {
            throw new ArgumentException("A promotion target is unavailable.");
        }

        if (request.CategoryId.HasValue
            && !await db.Categories.AnyAsync(category =>
                category.Id == request.CategoryId && category.IsActive, ct))
        {
            throw new ArgumentException("A promotion target is unavailable.");
        }

        if (request.BrandId.HasValue
            && !await db.Brands.AnyAsync(brand =>
                brand.Id == request.BrandId && brand.IsActive, ct))
        {
            throw new ArgumentException("A promotion target is unavailable.");
        }

        if (request.SellerId.HasValue
            && !await db.Sellers.AnyAsync(seller =>
                seller.Id == request.SellerId && seller.Status == SellerStatus.ACTIVE, ct))
        {
            throw new ArgumentException("A promotion target is unavailable.");
        }
    }

    private async Task<Promotion> LoadPromotionAsync(Guid? id, CancellationToken ct)
    {
        if (!id.HasValue)
            return new Promotion { Id = Guid.NewGuid() };

        return await db.Promotions.SingleOrDefaultAsync(promotion => promotion.Id == id, ct)
            ?? throw new KeyNotFoundException();
    }

    private static void ValidateExpectedVersion(Guid? id, Guid? expectedVersion, Promotion promotion)
    {
        if (id.HasValue && expectedVersion != promotion.Version)
            throw new BusinessRuleException("The promotion changed. Reload it before saving.");
    }

    private async Task<List<Coupon>> LoadExistingCouponsAsync(Guid? id, CancellationToken ct)
    {
        if (!id.HasValue)
            return [];

        return await db.Coupons
            .Where(coupon => coupon.PromotionId == id)
            .ToListAsync(ct);
    }

    private static void ValidateExistingCoupons(List<CouponInput> inputs, List<Coupon> existingCoupons)
    {
        foreach (var input in inputs)
        {
            var existingCoupon = input.Id.HasValue
                ? existingCoupons.SingleOrDefault(coupon => coupon.Id == input.Id)
                    ?? throw new ArgumentException("Coupon does not belong to this promotion.")
                : null;

            if (existingCoupon is not null
                && (existingCoupon.Code != input.Code.ToUpperInvariant()
                    || input.UsageLimit > 0 && input.UsageLimit < existingCoupon.UsedCount))
            {
                throw new BusinessRuleException(
                    "Existing coupon codes cannot be renamed, and limits cannot be lower than usage.");
            }

            if (input.UsageLimit > 0 && input.UsageLimitPerUser > input.UsageLimit)
                throw new ArgumentException("Per-user usage cannot exceed the global limit.");
        }
    }

    private static void UpdatePromotion(Promotion promotion, PromotionInput request, PromotionStatus status)
    {
        promotion.Name = request.Name.Trim();
        promotion.Description = request.Description?.Trim() ?? "";
        promotion.Type = Enum.Parse<PromotionType>(request.Type);
        promotion.Status = status;
        promotion.StartAt = request.StartAt.UtcDateTime;
        promotion.EndAt = request.EndAt.UtcDateTime;
        promotion.Version = Guid.NewGuid();
    }

    private async Task ReplaceRulesAsync(Promotion promotion, PromotionInput request, CancellationToken ct)
    {
        var oldRules = await db.PromotionRules
            .Where(rule => rule.PromotionId == promotion.Id)
            .ToListAsync(ct);

        db.PromotionRules.RemoveRange(oldRules);

        AddRuleIfPresent(promotion, PromotionRuleType.PRODUCT, request.ProductId?.ToString());
        AddRuleIfPresent(promotion, PromotionRuleType.CATEGORY, request.CategoryId?.ToString());
        AddRuleIfPresent(promotion, PromotionRuleType.BRAND, request.BrandId?.ToString());
        AddRuleIfPresent(promotion, PromotionRuleType.SELLER, request.SellerId?.ToString());

        var minimumOrderAmount = request.MinimumOrderAmount?.ToString(CultureInfo.InvariantCulture);
        AddRuleIfPresent(promotion, PromotionRuleType.MIN_ORDER_AMOUNT, minimumOrderAmount);
    }

    private void AddRuleIfPresent(Promotion promotion, PromotionRuleType ruleType, string? value)
    {
        if (value is null)
            return;

        db.PromotionRules.Add(new PromotionRule
        {
            Id = Guid.NewGuid(),
            PromotionId = promotion.Id,
            RuleType = ruleType,
            RuleValue = value
        });
    }

    private async Task ReplaceActionAsync(Promotion promotion, PromotionInput request, CancellationToken ct)
    {
        var oldActions = await db.PromotionActions
            .Where(action => action.PromotionId == promotion.Id)
            .ToListAsync(ct);

        db.PromotionActions.RemoveRange(oldActions);
        db.PromotionActions.Add(new PromotionAction
        {
            Id = Guid.NewGuid(),
            PromotionId = promotion.Id,
            DiscountType = request.Type,
            DiscountValue = request.DiscountValue,
            MaximumDiscount = request.MaximumDiscount
        });
    }

    private void UpdateCoupons(Promotion promotion, List<CouponInput> inputs, List<Coupon> existingCoupons)
    {
        foreach (var existingCoupon in existingCoupons)
            existingCoupon.Active = false;

        foreach (var input in inputs)
        {
            var coupon = input.Id.HasValue
                ? existingCoupons.Single(existingCoupon => existingCoupon.Id == input.Id)
                : new Coupon
                {
                    Id = Guid.NewGuid(),
                    PromotionId = promotion.Id,
                    Code = input.Code.ToUpperInvariant()
                };

            if (!input.Id.HasValue)
                db.Coupons.Add(coupon);

            coupon.Active = input.Active;
            coupon.UsageLimit = input.UsageLimit;
            coupon.UsageLimitPerUser = input.UsageLimitPerUser;
            coupon.StartAt = promotion.StartAt;
            coupon.EndAt = promotion.EndAt;
        }
    }

    private static bool Money(decimal value) => decimal.Round(value, 2) == value;

    private static PromotionView View(
        Promotion promotion,
        PromotionAction? action,
        IEnumerable<PromotionRule> rules,
        IEnumerable<Coupon> coupons)
    {
        var ruleList = rules.ToList();
        var minimumOrderAmount = FindMinimumOrderAmount(ruleList);
        var couponViews = coupons
            .OrderBy(coupon => coupon.Code)
            .Select(coupon => new CouponView(
                coupon.Id,
                coupon.Code,
                coupon.UsageLimit,
                coupon.UsageLimitPerUser,
                coupon.UsedCount,
                coupon.Active))
            .ToList();

        return new PromotionView(
            promotion.Id,
            promotion.Name,
            promotion.Description,
            promotion.Type.ToString(),
            promotion.Status.ToString(),
            new(DateTime.SpecifyKind(promotion.StartAt, DateTimeKind.Utc)),
            new(DateTime.SpecifyKind(promotion.EndAt, DateTimeKind.Utc)),
            action?.DiscountValue ?? 0,
            action?.MaximumDiscount,
            minimumOrderAmount,
            FindTarget(ruleList, PromotionRuleType.PRODUCT),
            FindTarget(ruleList, PromotionRuleType.CATEGORY),
            FindTarget(ruleList, PromotionRuleType.BRAND),
            FindTarget(ruleList, PromotionRuleType.SELLER),
            promotion.Version,
            couponViews);
    }

    private static Guid? FindTarget(List<PromotionRule> rules, PromotionRuleType type)
    {
        var value = rules.FirstOrDefault(rule => rule.RuleType == type)?.RuleValue;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static decimal? FindMinimumOrderAmount(List<PromotionRule> rules)
    {
        var value = rules.FirstOrDefault(rule => rule.RuleType == PromotionRuleType.MIN_ORDER_AMOUNT)?.RuleValue;

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }
}
