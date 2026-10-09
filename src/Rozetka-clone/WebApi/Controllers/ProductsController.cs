using System.Security.Claims;
using Application.Abstractions;
using Application.Products;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Controllers
{
    [ApiController]
    [Authorize]
    public sealed class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;
        private readonly IApplicationDbContext _dbContext;

        public ProductsController(
            IProductService productService,
            IApplicationDbContext dbContext
        )
        {
            _productService = productService;
            _dbContext = dbContext;
        }

        [HttpGet("/api/v1/products")]
        [AllowAnonymous]
        public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetProducts(
            CancellationToken cancellationToken
        )
        {
            var products = await _productService.GetAllAsync(cancellationToken);

            return Ok(products);
        }

        [HttpGet("/api/v1/products/{slug}")]
        [AllowAnonymous]
        public async Task<ActionResult<ProductDto>> GetProductBySlug(
            string slug,
            CancellationToken cancellationToken
        )
        {
            var product = await _productService.GetBySlugAsync(
                slug,
                cancellationToken
            );

            if (product is null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        [HttpPost("/api/v1/seller/products")]
        [Authorize(Roles = Roles.Administrator)]
        public async Task<ActionResult<ProductDto>> CreateProduct(
            [FromBody] CreateProductRequest request,
            CancellationToken cancellationToken
        )
        {
            var product = await _productService.CreateAsync(
                request,
                cancellationToken
            );

            return Created(
                $"/api/v1/products/{product.Slug}",
                product
            );
        }

        [HttpPatch("/api/v1/seller/products/{id:guid}")]
        [Authorize(Roles = Roles.Administrator)]
        public async Task<ActionResult<ProductDto>> UpdateProduct(
            Guid id,
            [FromBody] UpdateProductRequest request,
            CancellationToken cancellationToken
        )
        {
            var product = await _productService.UpdateAsync(
                id,
                request,
                cancellationToken
            );

            if (product is null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        [HttpPost("/api/v1/seller/products/{id:guid}/submit")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<IActionResult> SubmitForModeration(
            Guid id,
            CancellationToken cancellationToken
        )
        {
            if (User.IsInRole(Roles.Seller)
                && !await OwnsProductAsync(
                    id,
                    cancellationToken
                ))
            {
                return NotFound();
            }

            var result = await _productService.SubmitForModerationAsync(
                id,
                cancellationToken
            );

            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }

        [HttpPost("/api/v1/admin/products/{id:guid}/approve")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator}")]
        public async Task<IActionResult> ApproveProduct(
            Guid id,
            CancellationToken cancellationToken
        )
        {
            var result = await _productService.ApproveAsync(
                id,
                cancellationToken
            );

            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }

        [HttpPost("/api/v1/admin/products/{id:guid}/reject")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Moderator}")]
        public async Task<IActionResult> RejectProduct(
            Guid id,
            CancellationToken cancellationToken
        )
        {
            var result = await _productService.RejectAsync(
                id,
                cancellationToken
            );

            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }

        private async Task<bool> OwnsProductAsync(
            Guid productId,
            CancellationToken ct
        )
        {
            if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId
            ))
            {
                return false;
            }

            return await _dbContext.Products.AnyAsync(
                product =>
                    product.Id == productId
                    && _dbContext.Stores.Any(
                        store =>
                            store.Id == product.StoreId
                            && _dbContext.Sellers.Any(
                                seller =>
                                    seller.Id == store.SellerId
                                    && seller.UserId == userId
                            )
                    ),
                ct
            );
        }
    }
}
