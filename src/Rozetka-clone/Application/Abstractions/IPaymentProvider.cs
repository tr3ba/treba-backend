using Application.Payments;

namespace Application.Abstractions;

public interface IPaymentProvider
{
    Task<PaymentProviderResponse> CreatePaymentAsync(PaymentProviderRequest request, CancellationToken cancellationToken = default);
    Task<PaymentProviderStatus> GetStatusAsync(string externalPaymentId, CancellationToken cancellationToken = default);
    Task<RefundProviderResponse> RefundAsync(RefundProviderRequest request, CancellationToken cancellationToken = default);
    bool VerifyWebhook(string payload, string signature);
}