namespace Contracts.Marketplace;

public sealed record NotificationSettingsView(bool OrdersEmail, bool OrdersTelegram, bool SupportEmail,
    bool SupportTelegram, Guid Version, bool EmailAvailable, bool TelegramAvailable);
public sealed record SaveNotificationSettingsRequest(bool OrdersEmail, bool OrdersTelegram, bool SupportEmail,
    bool SupportTelegram, Guid Version);
