using Application.Products;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    public sealed class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;

        public ProductsController(IProductService productService)
        {
            _productService = productService;
        }

        [HttpGet("/api/v1/products")]
        public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetProducts(
            CancellationToken cancellationToken)
        {
            var products = await _productService.GetAllAsync(
                cancellationToken);

            return Ok(products);
        }

        [HttpGet("/api/v1/products/{slug}")]
        public async Task<ActionResult<ProductDto>> GetProductBySlug(
            string slug,
            CancellationToken cancellationToken)
        {
            var product = await _productService.GetBySlugAsync(
                slug,
                cancellationToken);

            if (product is null)
                return NotFound();

            return Ok(product);
        }

        [HttpPost("/api/v1/seller/products")]
        public async Task<ActionResult<ProductDto>> CreateProduct(
            [FromBody] CreateProductRequest request,
            CancellationToken cancellationToken)
        {
            var product = await _productService.CreateAsync(
                request,
                cancellationToken);

            return Created(
                $"/api/v1/products/{product.Slug}",
                product);
        }

        [HttpPatch("/api/v1/seller/products/{id:guid}")]
        public async Task<ActionResult<ProductDto>> UpdateProduct(
            Guid id,
            [FromBody] UpdateProductRequest request,
            CancellationToken cancellationToken)
        {
            var product = await _productService.UpdateAsync(
                id,
                request,
                cancellationToken);

            if (product is null)
                return NotFound();

            return Ok(product);
        }

        [HttpPost("/api/v1/seller/products/{id:guid}/submit")]
        public async Task<IActionResult> SubmitForModeration(
            Guid id,
            CancellationToken cancellationToken)
        {
            var result = await _productService
                .SubmitForModerationAsync(id, cancellationToken);

            if (!result)
                return NotFound();

            return NoContent();
        }

        [HttpPost("/api/v1/admin/products/{id:guid}/approve")]
        public async Task<IActionResult> ApproveProduct(
            Guid id,
            CancellationToken cancellationToken)
        {
            var result = await _productService
                .ApproveAsync(id, cancellationToken);

            if (!result)
                return NotFound();

            return NoContent();
        }

        [HttpPost("/api/v1/admin/products/{id:guid}/reject")]
        public async Task<IActionResult> RejectProduct(
            Guid id,
            CancellationToken cancellationToken)
        {
            var result = await _productService
                .RejectAsync(id, cancellationToken);

            if (!result)
                return NotFound();

            return NoContent();
        }
    }
}
