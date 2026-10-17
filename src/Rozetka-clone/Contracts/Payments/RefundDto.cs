namespace Contracts.Payments;

public sealed record RefundDto(
    Guid Id,
    Guid PaymentId,
    decimal Amount,
    string Status,
    DateTimeOffset CreatedAt);