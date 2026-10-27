namespace Domain.Entities;

public sealed class CustomerNotificationSettings
{
    public Guid UserId { get; set; }
    public bool OrdersEmail { get; set; }
    public bool OrdersTelegram { get; set; }
    public bool SupportEmail { get; set; }
    public bool SupportTelegram { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class CustomerNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string EventKey { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Kind { get; set; } = "";
    public Guid ResourceId { get; set; }
    public Guid SourceId { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Attempts { get; set; }
    public string? Result { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
