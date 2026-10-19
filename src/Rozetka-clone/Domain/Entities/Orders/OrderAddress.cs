namespace Domain.Entities.Orders;

public class OrderAddress 
{
    public Guid Id { get; set; }
    
    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    public string Country { get; set; } = null!;

    public string? Region { get; set; }

    public string City { get; set; } = null!;

    public string Street { get; set; } = null!;

    public string Building { get; set; } = null!;

    public string? Apartment { get; set; }

    public string? PostalCode { get; set; }

    public string RecipientName { get; set; } = null!;

    public string RecipientPhone { get; set; } = null!;
}