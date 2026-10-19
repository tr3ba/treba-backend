using Domain.Enums;

namespace Domain.Entities.Orders;

public class Order 
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;

    public Guid CustomerId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.CREATED;

    // TODO: после добавления Payment и Delivery убрать коменты
    // public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.PENDING;
    //
    // public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.CREATED;

    public string Currency { get; set; } = "UAH";

    public decimal Subtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal DeliveryPrice { get; set; }

    public decimal TotalAmount { get; set; }

    public string? CustomerComment { get; set; }

    public string RecipientName { get; set; } = null!;

    public string RecipientPhone { get; set; } = null!;

    public string RecipientEmail { get; set; } = null!;
    
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public byte[] Version { get; set; } = [];
    
    public OrderAddress? Address { get; set; }
    
    public ICollection<OrderItem> Items { get; set; } = [];
    
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = [];
}