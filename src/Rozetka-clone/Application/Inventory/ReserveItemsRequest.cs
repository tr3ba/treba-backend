namespace Application.Inventory;

public sealed record ReserveItemsRequest(
    Guid OrderId,
    List<OrderItemRequest> Items
);
