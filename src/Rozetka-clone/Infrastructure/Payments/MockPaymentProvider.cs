using Application.Abstractions;
using Application.Payments;

namespace Infrastructure.Payments;

public class MockPaymentProvider : IPaymentProvider
{
    public Task<PaymentProviderResponse> CreatePaymentAsync(PaymentProviderRequest request, CancellationToken cancellationToken = default)
    {
        bool isSuccessful = (int)request.Amount % 2 == 0;

        var response = new PaymentProviderResponse(
            Success: isSuccessful,
            ExternalPaymentId: isSuccessful ? $"mock_{Guid.NewGuid()}" : null,
            PaymentUrl: isSuccessful ? $"https://mock-payment-gateway.com/pay/{Guid.NewGuid()}" : null,
            Amount: request.Amount,
            Currency: request.Currency,
            ErrorMessage: isSuccessful ? null : "Mock payment failed: insufficient funds"
        );

        return Task.FromResult(response);
    }

    public Task<PaymentProviderStatus> GetStatusAsync(string externalPaymentId, CancellationToken cancellationToken = default)
    {
        bool isSuccessful = !externalPaymentId.Contains("failed");

        var status = new PaymentProviderStatus(
            Status: isSuccessful ? "success" : "failed",
            Amount: 0,
            ExternalTransactionId: isSuccessful ? $"txn_{Guid.NewGuid()}" : null,
            ErrorMessage: isSuccessful ? null : "Mock payment failed"
        );

        return Task.FromResult(status);
    }

    public Task<RefundProviderResponse> RefundAsync(RefundProviderRequest request, CancellationToken cancellationToken = default)
    {
        var response = new RefundProviderResponse(
            Success: true,
            ExternalTransactionId: $"refund_txn_{Guid.NewGuid()}",
            Status: "success",
            Amount: request.Amount,
            ErrorMessage: null,
            RequestData: $"{{ \"amount\": {request.Amount} }}",
            ResponseData: $"{{ \"status\": \"success\", \"transaction_id\": \"refund_txn_{Guid.NewGuid()}\" }}"
        );

        return Task.FromResult(response);
    }

    public bool VerifyWebhook(string payload, string signature)
    {
        return true;
    }
}
