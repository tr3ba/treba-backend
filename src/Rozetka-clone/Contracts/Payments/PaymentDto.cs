namespace Contracts.Payments;

public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    string Provider,
    string? ExternalPaymentId,
    string Status,
    decimal Amount,
    string Currency,
    string? PaymentUrl,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ExpiresAt);