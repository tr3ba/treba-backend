namespace Domain.Entities.Orders;

public class OrderItem 
{
    public Guid Id { get; set; }
    
    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    public Guid ProductId { get; set; }

    public Guid VariantId { get; set; }

    public Guid SellerId { get; set; }

    public string ProductName { get; set; } = null!;

    public string VariantName { get; set; } = null!;

    public string Sku { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TotalPrice { get; set; }

    public string? ProductSnapshot { get; set; }
}