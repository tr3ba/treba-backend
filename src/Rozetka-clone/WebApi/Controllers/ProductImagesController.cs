using Application.ProductImages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/products/{productId:guid}/images")]
    public sealed class ProductImagesController : ControllerBase
    {
        private readonly IProductImageService _productImageService;

        public ProductImagesController(
            IProductImageService productImageService)
        {
            _productImageService = productImageService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ProductImageDto>>> GetAll(
            Guid productId,
            CancellationToken cancellationToken)
        {
            var images = await _productImageService.GetByProductIdAsync(
                productId,
                cancellationToken);

            return Ok(images);
        }

        [HttpGet("{imageId:guid}")]
        public async Task<ActionResult<ProductImageDto>> GetById(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken)
        {
            var image = await _productImageService.GetByIdAsync(
                productId,
                imageId,
                cancellationToken);

            if (image is null)
                return NotFound();

            return Ok(image);
        }

        [HttpPost]
        public async Task<ActionResult<ProductImageDto>> Create(
            Guid productId,
            [FromBody] CreateProductImageRequest request,
            CancellationToken cancellationToken)
        {
            var image = await _productImageService.CreateAsync(
                productId,
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    productId,
                    imageId = image.Id
                },
                image);
        }

        [HttpPatch("{imageId:guid}")]
        public async Task<ActionResult<ProductImageDto>> Update(
            Guid productId,
            Guid imageId,
            [FromBody] UpdateProductImageRequest request,
            CancellationToken cancellationToken)
        {
            var image = await _productImageService.UpdateAsync(
                productId,
                imageId,
                request,
                cancellationToken);

            if (image is null)
                return NotFound();

            return Ok(image);
        }

        [HttpDelete("{imageId:guid}")]
        public async Task<IActionResult> Delete(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken)
        {
            var deleted = await _productImageService.DeleteAsync(
                productId,
                imageId,
                cancellationToken);

            if (!deleted)
                return NotFound();

            return NoContent();
        }
    }
}
