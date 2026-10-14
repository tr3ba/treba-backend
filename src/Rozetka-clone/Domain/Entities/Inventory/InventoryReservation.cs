using Domain.Enums;

namespace Domain.Entities.Inventory;

public class InventoryReservation
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid VariantId { get; set; }

    public Guid WarehouseId { get; set; }

    public int Quantity { get; set; }

    public ReservationStatus Status { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
