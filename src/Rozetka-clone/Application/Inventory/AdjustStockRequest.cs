namespace Application.Inventory;

public sealed record AdjustStockRequest(
    Guid WarehouseId,
    Guid VariantId,
    int Quantity
);
