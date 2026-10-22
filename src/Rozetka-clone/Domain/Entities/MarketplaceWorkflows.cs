namespace Domain.Entities;

public sealed class SellerApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string CompanyName { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string TaxNumber { get; set; } = "";
    public string? Website { get; set; }
    public string ContactName { get; set; } = "";
    public string Position { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsNonResident { get; set; }
    public string Status { get; set; } = "pending";
    public string? ReviewNote { get; set; }
    public Guid? ReviewedBy { get; set; }
    public Guid? SellerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class CustomerPreferences
{
    public Guid UserId { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class NotificationReceipt
{
    public Guid UserId { get; set; }
    public string EventKey { get; set; } = "";
    public DateTimeOffset ReadAt { get; set; } = DateTimeOffset.UtcNow;
}
