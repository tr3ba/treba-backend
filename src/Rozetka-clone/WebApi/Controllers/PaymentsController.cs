using Application.Abstractions;
using Application.Payments;
using Contracts.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Route("api/v1/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("orders/{orderId:guid}/payments")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePayment(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var payment = await _paymentService.CreatePaymentAsync(orderId, cancellationToken);
            return CreatedAtAction(nameof(GetPaymentById), new { id = payment.Id }, payment);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var payment = await _paymentService.GetPaymentStatusAsync(id, cancellationToken);
            return Ok(payment);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("webhooks/{provider}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ProcessWebhook(
        [FromRoute] string provider,
        [FromBody] PaymentWebhookRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _paymentService.ProcessWebhookAsync(provider, request, cancellationToken);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("admin/orders/{orderId:guid}/refund")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(RefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefundPayment(
        [FromRoute] Guid orderId,
        [FromBody] decimal amount,
        CancellationToken cancellationToken)
    {
        try
        {
            var refund = await _paymentService.RefundAsync(orderId, amount, cancellationToken);
            return Ok(refund);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}