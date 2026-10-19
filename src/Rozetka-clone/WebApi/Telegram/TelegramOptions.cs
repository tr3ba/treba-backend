namespace WebApi.Telegram;

public sealed class TelegramOptions
{
    public bool Enabled { get; set; }
    public bool LongPollingEnabled { get; set; }
    public string BotToken { get; set; } = "";
    private string botUsername = "";
    public string BotUsername { get => botUsername; set => botUsername = (value ?? "").Trim().TrimStart('@'); }
    public string MiniAppUrl { get; set; } = "";
    public int InitDataMaxAgeMinutes { get; set; } = 30;
    public bool EnforceSellerCategories { get; set; } = true;
    public bool Configured => Enabled && !string.IsNullOrWhiteSpace(BotToken)
        && System.Text.RegularExpressions.Regex.IsMatch(BotUsername, @"^[A-Za-z0-9_]{2,29}bot$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        && Uri.TryCreate(MiniAppUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath.EndsWith("/telegram/", StringComparison.Ordinal);
}
