namespace Contracts.Payments;

public sealed record PaymentStatusDto(
    Guid Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? PaidAt,
    string? FailureReason);