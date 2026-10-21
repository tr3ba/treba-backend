namespace Contracts.Payments;

public sealed record PaymentWebhookRequest(
    string ProviderEventId,
    string Type,
    object Data,
    string Signature);