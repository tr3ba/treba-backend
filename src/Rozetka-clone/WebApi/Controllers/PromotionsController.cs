using Application.Promotions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace WebApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/v1/promotions")]
    public sealed class PromotionsController : ControllerBase
    {
        private readonly IDiscountService _discountService;

        public PromotionsController(IDiscountService discountService)
        {
            _discountService = discountService;
        }

        [HttpPost("validate-coupon")]
        public async Task<ActionResult<CouponValidationResult>> ValidateCoupon(
            [FromBody] ValidateCouponRequest request,
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            var result = await _discountService.ValidateCouponAsync(
                userId,
                request.CouponCode,
                cancellationToken
            );

            return Ok(result);
        }

        [HttpPost("calculate")]
        public async Task<ActionResult<DiscountResult>> Calculate(
            [FromBody] CalculateDiscountRequest request,
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            var result = await _discountService.CalculateDiscountAsync(
                userId,
                request.Items,
                request.CouponCode,
                cancellationToken
            );

            return Ok(result);
        }

        private Guid GetCurrentUserId()
        {
            var value =
                User.FindFirstValue("sub")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(value, out var userId))
            {
                throw new UnauthorizedAccessException(
                    "User identifier is missing from access token."
                );
            }

            return userId;
        }
    }

    public sealed record ValidateCouponRequest(string CouponCode);

    public sealed record CalculateDiscountRequest(
        IReadOnlyCollection<PriceItem> Items,
        string? CouponCode
    );

}
