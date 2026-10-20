namespace Domain.Entities.Support;

public sealed class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId
    {
        get; set;
    }
    public Guid CustomerId
    {
        get; set;
    }
    public string Kind { get; set; } = "QUESTION";
    public string Subject { get; set; } = "";
    public string Status { get; set; } = "OPEN";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class SupportMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId
    {
        get; set;
    }
    public Guid AuthorId
    {
        get; set;
    }
    public string AuthorName { get; set; } = "";
    public string AuthorRole { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Internal
    {
        get; set;
    }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
