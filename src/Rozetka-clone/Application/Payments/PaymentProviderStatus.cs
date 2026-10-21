namespace Application.Payments;

public sealed record PaymentProviderStatus(
    string Status,
    decimal Amount,
    string? ExternalTransactionId,
    string? ErrorMessage);