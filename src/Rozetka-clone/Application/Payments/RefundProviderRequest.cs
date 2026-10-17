namespace Application.Payments;

public sealed record RefundProviderRequest(
    string ExternalPaymentId,
    decimal Amount,
    string Currency,
    string IdempotencyKey);