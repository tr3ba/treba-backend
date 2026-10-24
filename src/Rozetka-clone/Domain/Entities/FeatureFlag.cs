namespace Domain.Entities;

public sealed class FeatureFlag
{
    public const string ChatDeletion = "support.chat-deletion";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public bool Enabled { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
}
