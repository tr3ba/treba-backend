namespace Domain.Entities.Inventory;

public class Warehouse
{
    public Guid Id { get; set; }

    public Guid SellerId { get; set; }

    public string Name { get; set; } = null!;

    public string Country { get; set; } = null!;

    public string City { get; set; } = null!;

    public string Address { get; set; } = null!;

    public bool Active { get; set; }
}
