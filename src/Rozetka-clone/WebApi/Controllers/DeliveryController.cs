using Application.Delivery;
using Contracts.Delivery;
using Domain.Entities;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/delivery")]
    public sealed class DeliveryController : ControllerBase
    {
        private readonly IDeliveryService _deliveryService;

        public DeliveryController(IDeliveryService deliveryService)
        {
            _deliveryService = deliveryService;
        }

        [HttpPost("options")]
        [Authorize]
        [ProducesResponseType(typeof(List<DeliveryOptionDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CalculateOptions(
            [FromBody] DeliveryCalculationRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var options = await _deliveryService.CalculateOptionsAsync(
                    request,
                    cancellationToken
                );

                return Ok(options);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }


        [HttpPost("orders/{orderId:guid}/shipments")]
        [Authorize]
        [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateShipment(
            [FromRoute] Guid orderId,
            [FromBody] CreateOrderShipmentRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var shipment = await _deliveryService.CreateShipmentAsync(
                    orderId,
                    request.DeliveryMethodId,
                    cancellationToken
                );

                return StatusCode(
                    StatusCodes.Status201Created,
                    shipment
                );
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new
                {
                    message = "Не удалось определить текущего пользователя."
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }



        [HttpGet("shipments/{shipmentId:guid}/tracking")]
        [Authorize]
        [ProducesResponseType(typeof(TrackingDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TrackShipment(
            [FromRoute] Guid shipmentId,
            CancellationToken cancellationToken)
        {
            try
            {
                var tracking = await _deliveryService.TrackShipmentAsync(
                    shipmentId,
                    cancellationToken
                );

                return Ok(tracking);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new
                {
                    message = "Не удалось определить текущего пользователя."
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }

        [HttpGet("pickup-points")]
        [Authorize]
        [ProducesResponseType(typeof(List<PickupPointDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetPickupPoints(
            [FromQuery] string city,
            CancellationToken cancellationToken)
        {
            try
            {
                var pickupPoints = await _deliveryService.GetPickupPointsAsync(
                    city,
                    cancellationToken
                );

                return Ok(pickupPoints);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }


        [HttpPatch("shipments/{shipmentId:guid}/status")]
        [Authorize(Policy = "AdminOnly")]
        [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateShipmentStatus(
            [FromRoute] Guid shipmentId,
            [FromBody] UpdateShipmentStatusRequest request,
            CancellationToken cancellationToken)
        {
            if (!Enum.TryParse<ShipmentStatus>(
                    request.Status,
                    ignoreCase: false,
                    out var status)
                || !Enum.IsDefined(status))
            {
                return BadRequest(new
                {
                    message = "Неизвестный статус отправления."
                });
            }

            try
            {
                var shipment = await _deliveryService.UpdateStatusAsync(
                    shipmentId,
                    status,
                    cancellationToken
                );

                return Ok(shipment);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new
                {
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }
    }
}
