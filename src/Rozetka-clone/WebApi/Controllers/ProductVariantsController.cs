using Application.ProductVariants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/products/{productId:guid}/variants")]
    public sealed class ProductVariantsController : ControllerBase
    {
        private readonly IProductVariantService _productVariantService;

        public ProductVariantsController(
            IProductVariantService productVariantService)
        {
            _productVariantService = productVariantService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ProductVariantDto>>> GetAll(
            Guid productId,
            CancellationToken cancellationToken)
        {
            var variants = await _productVariantService.GetByProductIdAsync(
                productId,
                cancellationToken);

            return Ok(variants);
        }

        [HttpGet("{variantId:guid}")]
        public async Task<ActionResult<ProductVariantDto>> GetById(
            Guid productId,
            Guid variantId,
            CancellationToken cancellationToken)
        {
            var variant = await _productVariantService.GetByIdAsync(
                productId,
                variantId,
                cancellationToken);

            if (variant is null)
                return NotFound();

            return Ok(variant);
        }

        [HttpPost]
        public async Task<ActionResult<ProductVariantDto>> Create(
            Guid productId,
            [FromBody] CreateProductVariantRequest request,
            CancellationToken cancellationToken)
        {
            var variant = await _productVariantService.CreateAsync(
                productId,
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    productId,
                    variantId = variant.Id
                },
                variant);
        }

        [HttpPatch("{variantId:guid}")]
        public async Task<ActionResult<ProductVariantDto>> Update(
            Guid productId,
            Guid variantId,
            [FromBody] UpdateProductVariantRequest request,
            CancellationToken cancellationToken)
        {
            var variant = await _productVariantService.UpdateAsync(
                productId,
                variantId,
                request,
                cancellationToken);

            if (variant is null)
                return NotFound();

            return Ok(variant);
        }
    }
}
