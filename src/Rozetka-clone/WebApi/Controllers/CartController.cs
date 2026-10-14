using System.Security.Claims;
using Application.Carts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/v1/cart")]
    public sealed class CartController : ControllerBase
    {
        private readonly ICartService _cartService;

        public CartController(
            ICartService cartService
        )
        {
            _cartService = cartService;
        }

        [HttpGet]
        public async Task<ActionResult<CartDto>> Get(
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            var cart = await _cartService.GetAsync(
                userId,
                cancellationToken
            );

            return Ok(cart);
        }

        [HttpPost("items")]
        public async Task<ActionResult<CartDto>> AddItem(
            AddCartItemRequest request,
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            var cart = await _cartService.AddItemAsync(
                userId,
                request,
                cancellationToken
            );

            return Ok(cart);
        }

        [HttpPatch("items/{cartItemId:guid}")]
        public async Task<ActionResult<CartDto>> UpdateItem(
            Guid cartItemId,
            UpdateCartItemRequest request,
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            var cart = await _cartService.UpdateItemAsync(
                userId,
                cartItemId,
                request,
                cancellationToken
            );

            return Ok(cart);
        }

        [HttpDelete("items/{cartItemId:guid}")]
        public async Task<ActionResult<CartDto>> RemoveItem(
            Guid cartItemId,
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            var cart = await _cartService.RemoveItemAsync(
                userId,
                cartItemId,
                cancellationToken
            );

            return Ok(cart);
        }

        [HttpDelete]
        public async Task<IActionResult> Clear(
            CancellationToken cancellationToken
        )
        {
            var userId = GetCurrentUserId();

            await _cartService.ClearAsync(
                userId,
                cancellationToken
            );

            return NoContent();
        }

        private Guid GetCurrentUserId()
        {
            var value = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(
                value,
                out var userId
            ))
            {
                throw new UnauthorizedAccessException("User identifier is missing from access token.");
            }

            return userId;
        }
    }
}
