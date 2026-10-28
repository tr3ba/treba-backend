using Application.Wishlists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace WebApi.Controllers
{
    [ApiController]
    [Authorize(Roles = "Customer")]
    [Route("api/v1/wishlist")]
    public sealed class WishlistController : ControllerBase
    {
        private readonly IWishlistService _wishlistService;

        public WishlistController(
            IWishlistService wishlistService
        )
        {
            _wishlistService = wishlistService;
        }

        [HttpGet]
        public async Task<ActionResult<WishlistDto>> Get(
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            var wishlist = await _wishlistService.GetDefaultWishlistAsync(
                userId,
                cancellationToken
            );

            return Ok(wishlist);
        }

        [HttpPost("products/{productId:guid}")]
        public async Task<IActionResult> AddProduct(
            Guid productId,
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            await _wishlistService.AddProductAsync(
                userId,
                productId,
                cancellationToken
            );

            return NoContent();
        }

        [HttpDelete("products/{productId:guid}")]
        public async Task<IActionResult> RemoveProduct(
            Guid productId,
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            await _wishlistService.RemoveProductAsync(
                userId,
                productId,
                cancellationToken
            );

            return NoContent();
        }

        private Guid GetCurrentUserId()
        {
            var value =
                User.FindFirstValue("sub")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(
                value,
                out var userId
            ))
            {
                throw new UnauthorizedAccessException(
                    "User identifier is missing from access token."
                );
            }

            return userId;
        }
    }
}
