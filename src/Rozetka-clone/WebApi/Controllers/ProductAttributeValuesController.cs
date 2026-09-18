using Application.ProductAttributeValues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/products/{productId:guid}/attribute-values")]
    public sealed class ProductAttributeValuesController : ControllerBase
    {
        private readonly IProductAttributeValueService _service;

        public ProductAttributeValuesController(
            IProductAttributeValueService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ProductAttributeValueDto>>> GetAll(
            Guid productId,
            CancellationToken cancellationToken)
        {
            var values = await _service.GetByProductIdAsync(
                productId,
                cancellationToken);

            return Ok(values);
        }

        [HttpGet("{valueId:guid}")]
        public async Task<ActionResult<ProductAttributeValueDto>> GetById(
            Guid productId,
            Guid valueId,
            CancellationToken cancellationToken)
        {
            var value = await _service.GetByIdAsync(
                productId,
                valueId,
                cancellationToken);

            if (value is null)
                return NotFound();

            return Ok(value);
        }

        [HttpPost]
        public async Task<ActionResult<ProductAttributeValueDto>> Create(
            Guid productId,
            [FromBody] CreateProductAttributeValueRequest request,
            CancellationToken cancellationToken)
        {
            var value = await _service.CreateAsync(
                productId,
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    productId,
                    valueId = value.Id
                },
                value);
        }

        [HttpPatch("{valueId:guid}")]
        public async Task<ActionResult<ProductAttributeValueDto>> Update(
            Guid productId,
            Guid valueId,
            [FromBody] UpdateProductAttributeValueRequest request,
            CancellationToken cancellationToken)
        {
            var value = await _service.UpdateAsync(
                productId,
                valueId,
                request,
                cancellationToken);

            if (value is null)
                return NotFound();

            return Ok(value);
        }

        [HttpDelete("{valueId:guid}")]
        public async Task<IActionResult> Delete(
            Guid productId,
            Guid valueId,
            CancellationToken cancellationToken)
        {
            var deleted = await _service.DeleteAsync(
                productId,
                valueId,
                cancellationToken);

            if (!deleted)
                return NotFound();

            return NoContent();
        }
    }
}
