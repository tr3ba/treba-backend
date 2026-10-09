using Application.Inventory;
using Domain.Entities.Inventory;
using Domain.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Route("api/v1/inventory")]
public sealed class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(
        IInventoryService inventoryService
    )
    {
        _inventoryService = inventoryService;
    }

    [HttpGet("variants/{variantId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(InventoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInventory(
        Guid variantId,
        CancellationToken cancellationToken
    )
    {
        var inventory = await _inventoryService.GetInventoryAsync(
            variantId,
            cancellationToken
        );
        if (inventory is null)
        {
            return NotFound(
                new
                {
                    message = $"Остатки для варианта товара {variantId} не найдены."
                }
            );
        }

        return Ok(inventory);
    }

    [HttpGet("variants/{variantId:guid}/availability")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckAvailability(
        Guid variantId,
        [FromQuery] int quantity = 1,
        CancellationToken cancellationToken = default
    )
    {
        var isAvailable = await _inventoryService.IsAvailableAsync(
            variantId,
            quantity,
            cancellationToken
        );
        return Ok(
            new
            {
                variantId,
                requestedQuantity = quantity,
                isAvailable,
            }
        );
    }

    [HttpPost("increase")]
    [Authorize(Roles = $"{Roles.Seller},{Roles.Manager},{Roles.Administrator}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IncreaseStock(
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _inventoryService.IncreaseStockAsync(
                request.VariantId,
                request.WarehouseId,
                request.Quantity,
                cancellationToken
            );

            return NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
    }

    [HttpPost("decrease")]
    [Authorize(Roles = $"{Roles.Seller},{Roles.Manager},{Roles.Administrator}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DecreaseStock(
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _inventoryService.DecreaseStockAsync(
                request.VariantId,
                request.WarehouseId,
                request.Quantity,
                cancellationToken
            );

            return NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(
                new
                {
                    message = ex.Message
                }
            );
        }
    }

    [HttpPost("reserve")]
    [Authorize]
    [ProducesResponseType(typeof(List<InventoryReservation>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReserveItems(
        [FromBody] ReserveItemsRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var reservations = await _inventoryService.ReserveItemsAsync(
                request.OrderId,
                request.Items,
                cancellationToken
            );

            return Ok(reservations);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(
                new
                {
                    message = ex.Message
                }
            );
        }
    }

    [HttpPost("reservations/{orderId:guid}/confirm")]
    [Authorize(Roles = $"{Roles.Manager},{Roles.Administrator}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmReservation(
        Guid orderId,
        CancellationToken cancellationToken
    )
    {
        await _inventoryService.ConfirmReservationAsync(
            orderId,
            cancellationToken
        );
        return NoContent();
    }

    [HttpPost("reservations/{orderId:guid}/release")]
    [Authorize(Roles = $"{Roles.Manager},{Roles.Administrator}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReleaseReservation(
        Guid orderId,
        CancellationToken cancellationToken
    )
    {
        await _inventoryService.ReleaseReservationAsync(
            orderId,
            cancellationToken
        );
        return NoContent();
    }
}
