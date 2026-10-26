namespace Domain.Entities.Orders;

public sealed class OrderShipment
{
    public Guid OrderId { get; set; }
    public string Carrier { get; set; } = "NovaPoshta";
    public Guid CityRef { get; set; }
    public Guid PickupPointRef { get; set; }
    public string City { get; set; } = "";
    public string PickupPoint { get; set; } = "";
    public bool Postomat { get; set; }
    public string? Reference { get; set; }
    public decimal QuotedPrice { get; set; }
    public decimal WeightKg { get; set; }
    public bool EstimatedWeight { get; set; }

}
