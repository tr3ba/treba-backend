using Application.Brands;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/brands")]
    public sealed class BrandsController : ControllerBase
    {
        private readonly IBrandService _brandService;

        public BrandsController(IBrandService brandService)
        {
            _brandService = brandService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<BrandDto>>> GetAll(
            CancellationToken cancellationToken)
        {
            var brands = await _brandService.GetAllAsync(cancellationToken);

            return Ok(brands);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<BrandDto>> GetById(
            Guid id,
            CancellationToken cancellationToken)
        {
            var brand = await _brandService.GetByIdAsync(
                id,
                cancellationToken);

            if (brand is null)
                return NotFound();

            return Ok(brand);
        }

        [HttpPost]
        public async Task<ActionResult<BrandDto>> Create(
            [FromBody] CreateBrandRequest request,
            CancellationToken cancellationToken)
        {
            var brand = await _brandService.CreateAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = brand.Id },
                brand);
        }

        [HttpPatch("{id:guid}")]
        public async Task<ActionResult<BrandDto>> Update(
            Guid id,
            [FromBody] UpdateBrandRequest request,
            CancellationToken cancellationToken)
        {
            var brand = await _brandService.UpdateAsync(
                id,
                request,
                cancellationToken);

            if (brand is null)
                return NotFound();

            return Ok(brand);
        }
    }
}