using Domain.Entities.Inventory;

namespace Application.Inventory;

public interface IInventoryService
{
    Task<InventoryDto?> GetInventoryAsync(
        Guid variantId,
        CancellationToken cancellationToken = default
    );

    Task IncreaseStockAsync(
        Guid variantId,
        Guid warehouseId,
        int quantity,
        CancellationToken cancellationToken = default
    );

    Task DecreaseStockAsync(
        Guid variantId,
        Guid warehouseId,
        int quantity,
        CancellationToken cancellationToken = default
    );

    Task<List<InventoryReservation>> ReserveItemsAsync(
        Guid orderId,
        List<OrderItemRequest> items,
        CancellationToken cancellationToken = default
    );

    Task ConfirmReservationAsync(
        Guid orderId,
        CancellationToken cancellationToken = default
    );

    Task ReleaseReservationAsync(
        Guid orderId,
        CancellationToken cancellationToken = default
    );

    Task<bool> IsAvailableAsync(
        Guid variantId,
        int quantity,
        CancellationToken cancellationToken = default
    );
}
