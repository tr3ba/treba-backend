using Application.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/attributes")]
    public sealed class AttributesController : ControllerBase
    {
        private readonly IAttributeService _attributeService;

        public AttributesController(IAttributeService attributeService)
        {
            _attributeService = attributeService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<AttributeDto>>> GetByCategory(
            [FromQuery] Guid categoryId,
            CancellationToken cancellationToken)
        {
            var attributes = await _attributeService.GetByCategoryAsync(
                categoryId,
                cancellationToken);

            return Ok(attributes);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<AttributeDto>> GetById(
            Guid id,
            CancellationToken cancellationToken)
        {
            var attribute = await _attributeService.GetByIdAsync(
                id,
                cancellationToken);

            if (attribute is null)
                return NotFound();

            return Ok(attribute);
        }

        [HttpPost]
        public async Task<ActionResult<AttributeDto>> Create(
            [FromBody] CreateAttributeRequest request,
            CancellationToken cancellationToken)
        {
            var attribute = await _attributeService.CreateAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = attribute.Id },
                attribute);
        }

        [HttpPatch("{id:guid}")]
        public async Task<ActionResult<AttributeDto>> Update(
            Guid id,
            [FromBody] UpdateAttributeRequest request,
            CancellationToken cancellationToken)
        {
            var attribute = await _attributeService.UpdateAsync(
                id,
                request,
                cancellationToken);

            if (attribute is null)
                return NotFound();

            return Ok(attribute);
        }

        [HttpGet("{attributeId:guid}/options")]
        public async Task<ActionResult<IReadOnlyList<AttributeOptionDto>>> GetOptions(
            Guid attributeId,
            CancellationToken cancellationToken)
        {
            var options = await _attributeService.GetOptionsAsync(
                attributeId,
                cancellationToken);

            return Ok(options);
        }

        [HttpPost("{attributeId:guid}/options")]
        public async Task<ActionResult<AttributeOptionDto>> CreateOption(
            Guid attributeId,
            [FromBody] CreateAttributeOptionRequest request,
            CancellationToken cancellationToken)
        {
            var option = await _attributeService.CreateOptionAsync(
                attributeId,
                request,
                cancellationToken);

            return Created(
                $"/api/v1/attributes/{attributeId}/options/{option.Id}",
                option);
        }

        [HttpPatch("{attributeId:guid}/options/{optionId:guid}")]
        public async Task<ActionResult<AttributeOptionDto>> UpdateOption(
            Guid attributeId,
            Guid optionId,
            [FromBody] UpdateAttributeOptionRequest request,
            CancellationToken cancellationToken)
        {
            var option = await _attributeService.UpdateOptionAsync(
                attributeId,
                optionId,
                request,
                cancellationToken);

            if (option is null)
                return NotFound();

            return Ok(option);
        }
    }
}
