namespace Domain.Entities.Payments;

public class PaymentTransaction
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Type { get; set; } = null!;
    public string? ExternalTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = null!;
    public string? RequestData { get; set; }
    public string? ResponseData { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual Payment Payment { get; set; } = null!;
}
