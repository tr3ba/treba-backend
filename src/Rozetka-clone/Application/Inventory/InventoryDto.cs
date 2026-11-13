namespace Application.Inventory;

public sealed record InventoryDto(
    Guid Id,
    Guid WarehouseId,
    Guid VariantId,
    long AvailableQuantity,
    long ReservedQuantity,
    long AvailableForSale,
    long MinimumQuantity
);
