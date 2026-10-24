using Application.Abstractions;
using Application.Payments;
using Contracts.Payments;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Tests;

// Order/support fixtures exercise checkout and stock without calling a payment provider.
internal sealed class TestPaymentService(IApplicationDbContext db) : IPaymentService
{
    public async Task<PaymentDto> CreatePaymentAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var order = await db.Orders.SingleAsync(o => o.Id == orderId && o.CustomerId == userId, cancellationToken);
        return new(
            Guid.NewGuid(),
            orderId,
            "fixture",
            null,
            "PENDING",
            order.TotalAmount,
            order.Currency,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null
        );
    }

    public Task<PaymentStatusDto> GetPaymentStatusAsync(
        Guid paymentId,
        Guid userId,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("Payment provider queries are outside these fixtures.");

    public Task ProcessWebhookAsync(
        string provider,
        PaymentWebhookRequest request,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("Payment provider webhooks are outside these fixtures.");

    public Task<RefundDto> RefundAsync(Guid orderId, decimal amount, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Payment provider refunds are outside these fixtures.");
}
