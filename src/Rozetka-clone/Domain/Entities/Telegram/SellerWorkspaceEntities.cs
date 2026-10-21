namespace Domain.Entities.Telegram;

public sealed class SellerImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public Guid UserId { get; set; }
    public string SourceUrl { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public string Status { get; set; } = "Queued";
    public string? Error { get; set; }
    public Guid? PreviewId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class TelegramNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public string ExpectedRole { get; set; } = "";
    public string ExpectedStatus { get; set; } = "";
    public string Text { get; set; } = "";
    public string View { get; set; } = "product";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public int Attempts { get; set; }
    public string? Result { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class ProductIdentity
{
    public Guid ProductId { get; set; }
    public string ManufacturerPartNumber { get; set; } = "";
    public string NormalizedPartNumber { get; set; } = "";
}
