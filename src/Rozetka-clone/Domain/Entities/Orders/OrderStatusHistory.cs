using Domain.Enums;

namespace Domain.Entities.Orders;

public class OrderStatusHistory 
{
    public Guid Id { get; set; }
    
    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    public OrderStatus PreviousStatus { get; set; }

    public OrderStatus NewStatus { get; set; }

    public string? Comment { get; set; }

    public Guid? ChangedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}