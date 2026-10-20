using System.Globalization;
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
    public async Task<PagedResponse<PromotionView>> ListAsync(int page, string? search, string? status, CancellationToken ct)
    {
        OrderService.ValidatePage(page, 12);
        var query = db.Promotions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<PromotionStatus>(status, out var state) || !Enum.IsDefined(state) || state.ToString() != status)
                throw new ArgumentException("Unknown promotion status.");
            query = query.Where(p => p.Status == state);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(p => p.Name.ToLower().Contains(term) || db.Coupons.Any(c => c.PromotionId == p.Id && c.Code.ToLower().Contains(term)));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(p => p.StartAt).ThenBy(p => p.Id).Skip((page - 1) * 12).Take(12).ToListAsync(ct);
        var ids = rows.Select(p => p.Id).ToList();
        var coupons = await db.Coupons.AsNoTracking().Where(c => ids.Contains(c.PromotionId)).ToListAsync(ct);
        var actions = await db.PromotionActions.AsNoTracking().Where(a => ids.Contains(a.PromotionId)).ToListAsync(ct);
        var rules = await db.PromotionRules.AsNoTracking().Where(r => ids.Contains(r.PromotionId)).ToListAsync(ct);
        return new(rows.Select(p => View(p, actions.FirstOrDefault(a => a.PromotionId == p.Id), rules.Where(r => r.PromotionId == p.Id), coupons.Where(c => c.PromotionId == p.Id))).ToList(), page, 12, count, (int)Math.Ceiling(count / 12d));
    }
    public async Task<PromotionView?> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Promotions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (p is null)
            return null;
        return View(p, await db.PromotionActions.AsNoTracking().FirstOrDefaultAsync(a => a.PromotionId == id, ct),
            await db.PromotionRules.AsNoTracking().Where(r => r.PromotionId == id).ToListAsync(ct), await db.Coupons.AsNoTracking().Where(c => c.PromotionId == id).ToListAsync(ct));
    }
    public async Task<PromotionView> SaveAsync(Guid? id, PromotionInput request, CancellationToken ct)
    {
        OrderService.ValidateText(request.Name, 255, "Promotion name");
        if (request.Description?.Length > 4000)
            throw new ArgumentException("Description is too long.");
        if (request.Type is not ("PERCENTAGE" or "FIXED_AMOUNT"))
            throw new ArgumentException("Choose a percentage or fixed amount discount.");
        if (!Enum.TryParse<PromotionStatus>(request.Status, out var status) || !Enum.IsDefined(status) || status.ToString() != request.Status)
            throw new ArgumentException("Unknown promotion status.");
        if (request.StartAt >= request.EndAt || request.StartAt == default || request.EndAt == default)
            throw new ArgumentException("The end must be after the start.");
        if (status == PromotionStatus.ACTIVE && (request.StartAt > DateTimeOffset.UtcNow || request.EndAt <= DateTimeOffset.UtcNow))
            throw new ArgumentException("An active promotion must be within its dates.");
        if (status == PromotionStatus.SCHEDULED && request.EndAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("A scheduled promotion must end in the future.");
        if (request.DiscountValue <= 0 || request.DiscountValue > 999999999.99m || request.Type == "PERCENTAGE" && request.DiscountValue > 100
            || !Money(request.DiscountValue) || request.MaximumDiscount is <= 0 or > 999999999.99m || request.MaximumDiscount.HasValue && !Money(request.MaximumDiscount.Value)
            || request.MinimumOrderAmount is < 0 or > 999999999.99m || request.MinimumOrderAmount.HasValue && !Money(request.MinimumOrderAmount.Value))
            throw new ArgumentException("Invalid discount or monetary limit.");
        if (request.Coupons is null || request.Coupons.Count is < 1 or > 30 || request.Coupons.Any(c => c is null || string.IsNullOrWhiteSpace(c.Code)
            || c.Code.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(c.Code, @"^[A-Za-z0-9_-]+$") || c.UsageLimit < 0 || c.UsageLimitPerUser < 0))
            throw new ArgumentException("Provide valid coupon codes and nonnegative usage limits.");
        var codes = request.Coupons.Select(c => c.Code.ToUpperInvariant()).ToList();
        if (codes.Distinct().Count() != codes.Count || request.Coupons.Where(c => c.Id.HasValue).Select(c => c.Id).Distinct().Count() != request.Coupons.Count(c => c.Id.HasValue))
            throw new ArgumentException("Duplicate coupons.");
        if (await db.Coupons.AnyAsync(c => codes.Contains(c.Code.ToUpper()) && c.PromotionId != id, ct))
            throw new BusinessRuleException("A coupon code is already in use.");
        if (request.ProductId.HasValue && !await db.Products.AnyAsync(x => x.Id == request.ProductId, ct)
            || request.CategoryId.HasValue && !await db.Categories.AnyAsync(x => x.Id == request.CategoryId && x.IsActive, ct)
            || request.BrandId.HasValue && !await db.Brands.AnyAsync(x => x.Id == request.BrandId && x.IsActive, ct)
            || request.SellerId.HasValue && !await db.Sellers.AnyAsync(x => x.Id == request.SellerId && x.Status == SellerStatus.ACTIVE, ct))
            throw new ArgumentException("A promotion target is unavailable.");
        var p = id.HasValue ? await db.Promotions.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new KeyNotFoundException() : new Promotion { Id = Guid.NewGuid() };
        if (id.HasValue && request.ExpectedVersion != p.Version)
            throw new BusinessRuleException("The promotion changed. Reload it before saving.");
        var existing = id.HasValue ? await db.Coupons.Where(c => c.PromotionId == id).ToListAsync(ct) : [];
        foreach (var input in request.Coupons)
        {
            var old = input.Id.HasValue ? existing.SingleOrDefault(c => c.Id == input.Id) ?? throw new ArgumentException("Coupon does not belong to this promotion.") : null;
            if (old is not null && (old.Code != input.Code.ToUpperInvariant() || input.UsageLimit > 0 && input.UsageLimit < old.UsedCount))
                throw new BusinessRuleException("Existing coupon codes cannot be renamed, and limits cannot be lower than usage.");
            if (input.UsageLimit > 0 && input.UsageLimitPerUser > input.UsageLimit)
                throw new ArgumentException("Per-user usage cannot exceed the global limit.");
        }
        if (!id.HasValue)
            db.Promotions.Add(p);
        p.Name = request.Name.Trim();
        p.Description = request.Description?.Trim() ?? "";
        p.Type = Enum.Parse<PromotionType>(request.Type);
        p.Status = status;
        p.StartAt = request.StartAt.UtcDateTime;
        p.EndAt = request.EndAt.UtcDateTime;
        p.Version = Guid.NewGuid();
        var oldRules = await db.PromotionRules.Where(r => r.PromotionId == p.Id).ToListAsync(ct);
        db.PromotionRules.RemoveRange(oldRules);
        void Rule(PromotionRuleType type, string value) => db.PromotionRules.Add(new()
        {
            Id = Guid.NewGuid(),
            PromotionId = p.Id,
            RuleType = type,
            RuleValue = value
        });
        if (request.ProductId.HasValue)
            Rule(PromotionRuleType.PRODUCT, request.ProductId.Value.ToString());
        if (request.CategoryId.HasValue)
            Rule(PromotionRuleType.CATEGORY, request.CategoryId.Value.ToString());
        if (request.BrandId.HasValue)
            Rule(PromotionRuleType.BRAND, request.BrandId.Value.ToString());
        if (request.SellerId.HasValue)
            Rule(PromotionRuleType.SELLER, request.SellerId.Value.ToString());
        if (request.MinimumOrderAmount.HasValue)
            Rule(PromotionRuleType.MIN_ORDER_AMOUNT, request.MinimumOrderAmount.Value.ToString(CultureInfo.InvariantCulture));
        db.PromotionActions.RemoveRange(await db.PromotionActions.Where(a => a.PromotionId == p.Id).ToListAsync(ct));
        db.PromotionActions.Add(new()
        {
            Id = Guid.NewGuid(),
            PromotionId = p.Id,
            DiscountType = request.Type,
            DiscountValue = request.DiscountValue,
            MaximumDiscount = request.MaximumDiscount
        });
        foreach (var old in existing)
            old.Active = false;
        foreach (var input in request.Coupons)
        {
            var coupon = input.Id.HasValue ? existing.Single(c => c.Id == input.Id) : new Coupon { Id = Guid.NewGuid(), PromotionId = p.Id, Code = input.Code.ToUpperInvariant() };
            if (!input.Id.HasValue)
                db.Coupons.Add(coupon);
            coupon.Active = input.Active;
            coupon.UsageLimit = input.UsageLimit;
            coupon.UsageLimitPerUser = input.UsageLimitPerUser;
            coupon.StartAt = p.StartAt;
            coupon.EndAt = p.EndAt;
        }
        await db.SaveChangesAsync(ct);
        return (await GetAsync(p.Id, ct))!;
    }
    private static bool Money(decimal value) => decimal.Round(value, 2) == value;
    private static PromotionView View(Promotion p, PromotionAction? a, IEnumerable<PromotionRule> rules, IEnumerable<Coupon> coupons)
    {
        var list = rules.ToList();
        Guid? Target(PromotionRuleType type) => Guid.TryParse(list.FirstOrDefault(r => r.RuleType == type)?.RuleValue, out var id) ? id : null;
        var min = decimal.TryParse(list.FirstOrDefault(r => r.RuleType == PromotionRuleType.MIN_ORDER_AMOUNT)?.RuleValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? (decimal?)value : null;
        return new(p.Id, p.Name, p.Description, p.Type.ToString(), p.Status.ToString(), new(DateTime.SpecifyKind(p.StartAt, DateTimeKind.Utc)), new(DateTime.SpecifyKind(p.EndAt, DateTimeKind.Utc)),
            a?.DiscountValue ?? 0, a?.MaximumDiscount, min, Target(PromotionRuleType.PRODUCT), Target(PromotionRuleType.CATEGORY), Target(PromotionRuleType.BRAND), Target(PromotionRuleType.SELLER), p.Version,
            coupons.OrderBy(c => c.Code).Select(c => new CouponView(c.Id, c.Code, c.UsageLimit, c.UsageLimitPerUser, c.UsedCount, c.Active)).ToList());
    }
}
