using Contracts.Payments;

namespace Application.Payments;

public interface IPaymentService
{
    Task<PaymentDto> CreatePaymentAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<PaymentStatusDto> GetPaymentStatusAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task ProcessWebhookAsync(string provider, PaymentWebhookRequest request, CancellationToken cancellationToken = default);
    Task<RefundDto> RefundAsync(Guid orderId, decimal amount, CancellationToken cancellationToken = default);
}