using Application.Categories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/categories")]
    public sealed class CategoriesController : ControllerBase
    {
        private readonly ICategoryService _categoryService;

        public CategoriesController(ICategoryService categoryService)
        {
            _categoryService = categoryService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<CategoryTreeDto>>> GetTree(
            CancellationToken cancellationToken)
        {
            var categories = await _categoryService.GetTreeAsync(cancellationToken);

            return Ok(categories);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<CategoryDto>> GetById(
            Guid id,
            CancellationToken cancellationToken)
        {
            var category = await _categoryService.GetByIdAsync(
                id,
                cancellationToken);

            if (category is null)
                return NotFound();

            return Ok(category);
        }

        [HttpPost]
        public async Task<ActionResult<CategoryDto>> Create(
            [FromBody] CreateCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var category = await _categoryService.CreateAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = category.Id },
                category);
        }

        [HttpPatch("{id:guid}")]
        public async Task<ActionResult<CategoryDto>> Update(
            Guid id,
            [FromBody] UpdateCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var category = await _categoryService.UpdateAsync(
                id,
                request,
                cancellationToken);

            if (category is null)
                return NotFound();

            return Ok(category);
        }

        [HttpPost("{id:guid}/move")]
        public async Task<IActionResult> Move(
            Guid id,
            [FromBody] MoveCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var moved = await _categoryService.MoveAsync(
                id,
                request.NewParentId,
                cancellationToken);

            if (!moved)
                return NotFound();

            return NoContent();
        }
    }
}