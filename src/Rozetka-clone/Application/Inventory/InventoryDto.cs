namespace Application.Inventory;

public sealed record InventoryDto(
    Guid Id,
    Guid WarehouseId,
    Guid VariantId,
    int AvailableQuantity,
    int ReservedQuantity,
    int AvailableForSale,
    int MinimumQuantity
);
