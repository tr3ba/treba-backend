using System.Security.Claims;
using Application.Sellers;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/sellers")]
    public sealed class SellersController : ControllerBase
    {
        private readonly ISellerService _sellerService;

        public SellersController(
            ISellerService sellerService
        )
        {
            _sellerService = sellerService;
        }

        [HttpPost]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<SellerDto>> Create(
            [FromBody] CreateSellerRequest request,
            CancellationToken cancellationToken
        )
        {
            if (
                User.IsInRole(Roles.Seller)
                && User.FindFirstValue(ClaimTypes.NameIdentifier) != request.UserId.ToString()
            )
            {
                return Forbid();
            }

            var seller = await _sellerService.CreateAsync(
                request,
                cancellationToken
            );

            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    id = seller.Id
                },
                seller
            );
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<SellerDto>> GetById(
            Guid id,
            CancellationToken cancellationToken
        )
        {
            var seller = await _sellerService.GetByIdAsync(
                id,
                cancellationToken
            );

            if (seller is null)
            {
                return NotFound();
            }

            return Ok(seller);
        }

        [HttpPatch("{id:guid}")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<SellerDto>> Update(
            Guid id,
            [FromBody] UpdateSellerRequest request,
            CancellationToken cancellationToken
        )
        {
            if (User.IsInRole(Roles.Seller))
            {
                var current = await _sellerService.GetByIdAsync(
                    id,
                    cancellationToken
                );
                if (current is null)
                {
                    return NotFound();
                }

                if (User.FindFirstValue(ClaimTypes.NameIdentifier) != current.UserId.ToString())
                {
                    return Forbid();
                }
            }
            var seller = await _sellerService.UpdateAsync(
                id,
                request,
                cancellationToken
            );

            if (seller is null)
            {
                return NotFound();
            }

            return Ok(seller);
        }
    }
}
