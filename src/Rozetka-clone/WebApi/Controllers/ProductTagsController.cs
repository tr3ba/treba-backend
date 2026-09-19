using Application.ProductTags;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/product-tags")]
    public sealed class ProductTagsController : ControllerBase
    {
        private readonly IProductTagService _service;

        public ProductTagsController(IProductTagService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ProductTagDto>>> GetAll(
            CancellationToken cancellationToken)
        {
            var tags = await _service.GetAllAsync(cancellationToken);

            return Ok(tags);
        }

        [HttpGet("{tagId:guid}")]
        public async Task<ActionResult<ProductTagDto>> GetById(
            Guid tagId,
            CancellationToken cancellationToken)
        {
            var tag = await _service.GetByIdAsync(
                tagId,
                cancellationToken);

            if (tag is null)
                return NotFound();

            return Ok(tag);
        }

        [HttpPost]
        public async Task<ActionResult<ProductTagDto>> Create(
            [FromBody] CreateProductTagRequest request,
            CancellationToken cancellationToken)
        {
            var tag = await _service.CreateAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { tagId = tag.Id },
                tag);
        }

        [HttpPatch("{tagId:guid}")]
        public async Task<ActionResult<ProductTagDto>> Update(
            Guid tagId,
            [FromBody] UpdateProductTagRequest request,
            CancellationToken cancellationToken)
        {
            var tag = await _service.UpdateAsync(
                tagId,
                request,
                cancellationToken);

            if (tag is null)
                return NotFound();

            return Ok(tag);
        }

        [HttpDelete("{tagId:guid}")]
        public async Task<IActionResult> Delete(
            Guid tagId,
            CancellationToken cancellationToken)
        {
            var deleted = await _service.DeleteAsync(
                tagId,
                cancellationToken);

            if (!deleted)
                return NotFound();

            return NoContent();
        }

        [HttpGet("/api/v1/products/{productId:guid}/tags")]
        public async Task<ActionResult<IReadOnlyList<ProductTagDto>>> GetProductTags(
            Guid productId,
            CancellationToken cancellationToken)
        {
            var tags = await _service.GetProductTagsAsync(
                productId,
                cancellationToken);

            return Ok(tags);
        }

        [HttpPost("/api/v1/products/{productId:guid}/tags/{tagId:guid}")]
        public async Task<IActionResult> AddTagToProduct(
            Guid productId,
            Guid tagId,
            CancellationToken cancellationToken)
        {
            var added = await _service.AddTagToProductAsync(
                productId,
                tagId,
                cancellationToken);

            if (!added)
                return NoContent();

            return NoContent();
        }

        [HttpDelete("/api/v1/products/{productId:guid}/tags/{tagId:guid}")]
        public async Task<IActionResult> RemoveTagFromProduct(
            Guid productId,
            Guid tagId,
            CancellationToken cancellationToken)
        {
            var removed = await _service.RemoveTagFromProductAsync(
                productId,
                tagId,
                cancellationToken);

            if (!removed)
                return NotFound();

            return NoContent();
        }
    }
}
