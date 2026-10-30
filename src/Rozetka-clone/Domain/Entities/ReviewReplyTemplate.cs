namespace Domain.Entities;

public sealed class ReviewReplyTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string TextUk { get; set; } = "";
    public string TextEn { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
}
