namespace Infrastructure.Authentication;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public string Issuer { get; set; } = "TREBA";
    public string EncryptionKey { get; set; } = string.Empty;
    public string OtpHashKey { get; set; } = string.Empty;
    public int ChallengeLifetimeMinutes { get; set; } = 5;
}

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "TREBA Security";
}
