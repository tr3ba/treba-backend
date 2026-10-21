namespace Application.Payments;

public sealed record RefundProviderResponse(
    bool Success,
    string? ExternalTransactionId,
    string Status,
    decimal Amount,
    string? ErrorMessage,
    string? RequestData,
    string? ResponseData);