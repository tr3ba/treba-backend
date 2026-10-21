namespace Application.Payments;

public sealed record PaymentProviderRequest(
    Guid OrderId,
    decimal Amount,
    string Currency,
    string IdempotencyKey);