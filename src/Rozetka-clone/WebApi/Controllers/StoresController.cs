using System.Security.Claims;
using Application.Sellers;
using Application.Stores;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/sellers/{sellerId:guid}/stores")]
    public sealed class StoresController : ControllerBase
    {
        private readonly IStoreService _storeService;
        private readonly ISellerService _sellerService;

        public StoresController(
            IStoreService storeService,
            ISellerService sellerService
        )
        {
            _storeService = storeService;
            _sellerService = sellerService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<StoreDto>>> GetAll(
            Guid sellerId,
            CancellationToken cancellationToken
        )
        {
            var stores = await _storeService.GetBySellerIdAsync(
                sellerId,
                cancellationToken
            );

            return Ok(stores);
        }

        [HttpGet("{storeId:guid}")]
        public async Task<ActionResult<StoreDto>> GetById(
            Guid sellerId,
            Guid storeId,
            CancellationToken cancellationToken
        )
        {
            var store = await _storeService.GetByIdAsync(
                sellerId,
                storeId,
                cancellationToken
            );

            if (store is null)
            {
                return NotFound();
            }

            return Ok(store);
        }

        [HttpPost]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<StoreDto>> Create(
            Guid sellerId,
            [FromBody] CreateStoreRequest request,
            CancellationToken cancellationToken
        )
        {
            if (!await CanEditSellerAsync(
                sellerId,
                cancellationToken
            ))
            {
                return Forbid();
            }

            var store = await _storeService.CreateAsync(
                sellerId,
                request,
                cancellationToken
            );

            return CreatedAtAction(
                nameof(GetById),
                new
                {
                    sellerId,
                    storeId = store.Id
                },
                store
            );
        }

        [HttpPatch("{storeId:guid}")]
        [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager},{Roles.Seller}")]
        public async Task<ActionResult<StoreDto>> Update(
            Guid sellerId,
            Guid storeId,
            [FromBody] UpdateStoreRequest request,
            CancellationToken cancellationToken
        )
        {
            if (!await CanEditSellerAsync(
                sellerId,
                cancellationToken
            ))
            {
                return Forbid();
            }

            var store = await _storeService.UpdateAsync(
                sellerId,
                storeId,
                request,
                cancellationToken
            );

            if (store is null)
            {
                return NotFound();
            }

            return Ok(store);
        }

        private async Task<bool> CanEditSellerAsync(
            Guid sellerId,
            CancellationToken ct
        )
        {
            if (!User.IsInRole(Roles.Seller))
            {
                return true;
            }

            var seller = await _sellerService.GetByIdAsync(
                sellerId,
                ct
            );
            return seller is not null
                && User.FindFirstValue(ClaimTypes.NameIdentifier) == seller.UserId.ToString();
        }
    }
}
