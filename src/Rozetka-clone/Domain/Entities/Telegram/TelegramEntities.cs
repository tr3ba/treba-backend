namespace Domain.Entities.Telegram;

public sealed class TelegramBotCursor
{
    public long BotId { get; set; }
    public long LastUpdateId { get; set; } = -1;
}

public sealed class TelegramBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long TelegramUserId { get; set; }
    public long ChatId { get; set; }
    public string? Username { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TelegramLinkChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public long? TelegramUserId { get; set; }
    public long? ChatId { get; set; }
    public string? Username { get; set; }
    public string? TelegramName { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class TelegramGuardChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? LoginChallengeId { get; set; }
    public string BrowserSecretHash { get; set; } = "";
    public string ApprovalSecretHash { get; set; } = "";
    public int DisplayCode { get; set; }
    public string DeviceName { get; set; } = "";
    public string ClientIp { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? ApprovedUserId { get; set; }
    public long? ApprovedTelegramUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class SellerCategoryGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SellerId { get; set; }
    public Guid CategoryId { get; set; }
    public bool IncludeDescendants { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RozetkaCategoryMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ExternalKey { get; set; } = "";
    public Guid CategoryId { get; set; }
}

public sealed class ProductImportDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string SourceProductId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourceCategoryKey { get; set; } = "";
    public string SourceCategoryName { get; set; } = "";
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string BrandName { get; set; } = "";
    public string ImagesJson { get; set; } = "[]";
    public string CharacteristicsJson { get; set; } = "[]";
    public decimal? SourcePrice { get; set; }
    public Guid? ProductId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
