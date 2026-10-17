using Domain.Enums;

namespace Domain.Entities.Payments;

public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = null!;
    public string? ExternalPaymentId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.CREATED;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UAH";
    public string? PaymentUrl { get; set; }
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public virtual ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
}
