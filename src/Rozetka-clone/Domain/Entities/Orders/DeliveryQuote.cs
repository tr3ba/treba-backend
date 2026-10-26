namespace Domain.Entities.Orders;

public sealed class DeliveryQuote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Fingerprint { get; set; } = "";
    public Guid CityRef { get; set; }
    public Guid PickupPointRef { get; set; }
    public string City { get; set; } = "";
    public string PickupPoint { get; set; } = "";
    public bool Postomat { get; set; }
    public decimal Price { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DeclaredValue { get; set; }
    public decimal WeightKg { get; set; }
    public bool EstimatedWeight { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? UsedByOrderId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
