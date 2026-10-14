namespace Domain.Entities.Inventory;

public class Inventory
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid VariantId { get; set; }
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int MinimumQuantity { get; set; }
    public byte[] Version { get; private set; } = [];
    public int AvailableForSale
    {
        get
        {
            return Math.Max(
                0,
                AvailableQuantity - ReservedQuantity
            );
        }
    }
}
