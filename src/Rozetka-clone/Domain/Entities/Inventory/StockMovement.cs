using Domain.Enums;

namespace Domain.Entities.Inventory;

public class StockMovement
{
    public Guid Id { get; set; }

    public Guid InventoryId { get; set; }

    public StockMovementType Type { get; set; }

    public int Quantity { get; set; }

    public string Reason { get; set; } = null!;

    public string? ReferenceType { get; set; }

    public Guid? ReferenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
