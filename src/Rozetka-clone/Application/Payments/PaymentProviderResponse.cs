namespace Application.Payments;

public sealed record PaymentProviderResponse(
    bool Success,
    string? ExternalPaymentId,
    string? PaymentUrl,
    decimal Amount,
    string Currency,
    string? ErrorMessage);