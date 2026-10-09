namespace Application.Inventory;

public sealed record OrderItemRequest(
    Guid VariantId,
    int Quantity
);
