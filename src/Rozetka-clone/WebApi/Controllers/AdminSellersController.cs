using Application.Sellers;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Contracts.Telegram;
using Infrastructure.Administration;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/admin/sellers")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Manager}")]
    public sealed class AdminSellersController : ControllerBase
    {
        private readonly ISellerService _sellerService;

        public AdminSellersController(
            ISellerService sellerService
        )
        {
            _sellerService = sellerService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<SellerDto>>> GetAll(
            CancellationToken cancellationToken
        )
        {
            var sellers = await _sellerService.GetAllAsync(cancellationToken);

            return Ok(sellers);
        }

        [HttpPost, Authorize(Roles = Roles.Administrator)]
        public async Task<ActionResult<SellerWorkspaceCreatedDto>> Create(CreateSellerWorkspaceRequest request,
            [FromServices] SellerProvisioningService provisioning, CancellationToken ct)
        {
            var result = await provisioning.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.SellerId }, result);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<SellerDto>> GetById(Guid id, CancellationToken cancellationToken)
        {
            var seller = await _sellerService.GetByIdAsync(id, cancellationToken);
            return seller is null ? NotFound() : Ok(seller);
        }

        [HttpPost("{id:guid}/approve")]
        public async Task<ActionResult<SellerDto>> Approve(
            Guid id,
            CancellationToken cancellationToken
        )
        {
            var seller = await _sellerService.ApproveAsync(
                id,
                cancellationToken
            );

            if (seller is null)
            {
                return NotFound();
            }

            return Ok(seller);
        }

        [HttpPost("{id:guid}/suspend")]
        public async Task<ActionResult<SellerDto>> Suspend(
            Guid id,
            CancellationToken cancellationToken
        )
        {
            var seller = await _sellerService.SuspendAsync(
                id,
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
