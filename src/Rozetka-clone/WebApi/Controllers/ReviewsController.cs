using Application.Common;
using Application.Reviews;
using Domain.Entities.Users;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/reviews")]
    public sealed class ReviewsController : ControllerBase
    {
        private readonly IReviewService _reviewService;

        public ReviewsController(IReviewService reviewService)
        {
            _reviewService = reviewService;
        }

        [AllowAnonymous]
        [HttpGet("products/{productId:guid}")]
        public async Task<ActionResult<PagedResult<ReviewDto>>> GetProductReviews(
            Guid productId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var result = await _reviewService.GetProductReviewsAsync(
                productId,
                page,
                pageSize,
                cancellationToken);

            return Ok(result);
        }


        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator}")]
        [HttpPut("{reviewId:guid}/moderate")]
        public async Task<IActionResult> ModerateReview(
            Guid reviewId,
            [FromQuery] ReviewStatus status,
            CancellationToken cancellationToken)
        {
            await _reviewService.ModerateReviewAsync(
                reviewId,
                status,
                cancellationToken);

            return NoContent();
        }


        [Authorize]
        [HttpPut("{reviewId:guid}")]
        public async Task<ActionResult<ReviewDto>> UpdateReview(
            Guid reviewId,
            [FromBody] UpdateReviewRequest request,
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();

            var review = await _reviewService.UpdateReviewAsync(
                reviewId,
                userId,
                request,
                cancellationToken);

            return Ok(review);
        }

        [Authorize]
        [HttpDelete("{reviewId:guid}")]
        public async Task<IActionResult> DeleteReview(
            Guid reviewId,
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();

            await _reviewService.DeleteReviewAsync(
                reviewId,
                userId,
                cancellationToken);

            return NoContent();
        }

        [Authorize]
        [HttpPut("{reviewId:guid}/vote")]
        public async Task<IActionResult> VoteReview(
            Guid reviewId,
            [FromQuery] bool helpful,
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();

            await _reviewService.VoteReviewAsync(
                reviewId,
                userId,
                helpful,
                cancellationToken);

            return NoContent();
        }

        private Guid GetCurrentUserId()
        {
            var value =
                User.FindFirstValue("sub")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(value, out var userId))
            {
                throw new UnauthorizedAccessException(
                    "User identifier is missing from access token.");
            }

            return userId;
        }
    }
}
