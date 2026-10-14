using Application.Abstractions;
using Domain.Entities.Inventory;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Inventory;

public sealed class InventoryService : IInventoryService
{
    private readonly IApplicationDbContext _context;

    public InventoryService(
        IApplicationDbContext context
    )
    {
        _context = context;
    }

    public async Task<InventoryDto?> GetInventoryAsync(
        Guid variantId,
        CancellationToken cancellationToken = default
    )
    {
        var inventory = await _context
            .Inventory
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.VariantId == variantId,
                cancellationToken
            );

        if (inventory is null)
        {
            return null;
        }

        return new InventoryDto(
            inventory.Id,
            inventory.WarehouseId,
            inventory.VariantId,
            inventory.AvailableQuantity,
            inventory.ReservedQuantity,
            inventory.AvailableForSale,
            inventory.MinimumQuantity
        );
    }

    public async Task<bool> IsAvailableAsync(
        Guid variantId,
        int quantity,
        CancellationToken cancellationToken = default
    )
    {
        if (quantity <= 0)
        {
            return false;
        }

        var totalAvailable =
            await _context
                .Inventory
                .AsNoTracking()
                .Where(x => x.VariantId == variantId)
                .SumAsync(
                    x => (int?)(x.AvailableQuantity - x.ReservedQuantity),
                    cancellationToken
                )
            ?? 0;

        return totalAvailable >= quantity;
    }

    public async Task IncreaseStockAsync(
        Guid variantId,
        Guid warehouseId,
        int quantity,
        CancellationToken cancellationToken = default
    )
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Количество пополнения должно быть больше нуля."
            );
        }

        var inventory = await _context.Inventory.FirstOrDefaultAsync(
            x =>
                x.VariantId == variantId
                && x.WarehouseId == warehouseId,
            cancellationToken
        );

        if (inventory is null)
        {
            inventory = new Domain.Entities.Inventory.Inventory
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseId,
                VariantId = variantId,
                AvailableQuantity = quantity,
                ReservedQuantity = 0,
                MinimumQuantity = 0,
            };

            _context.Inventory.Add(inventory);
        }
        else
        {
            inventory.AvailableQuantity += quantity;
        }

        var movement = new StockMovement
        {
            Id = Guid.NewGuid(),
            InventoryId = inventory.Id,
            Type = StockMovementType.RECEIPT,
            Quantity = quantity,
            Reason = "Пополнение остатка на складе",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _context.StockMovements.Add(movement);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DecreaseStockAsync(
        Guid variantId,
        Guid warehouseId,
        int quantity,
        CancellationToken cancellationToken = default
    )
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Количество списания должно быть больше нуля."
            );
        }

        var inventory =
            await _context.Inventory.FirstOrDefaultAsync(
                x =>
                    x.VariantId == variantId
                    && x.WarehouseId == warehouseId,
                cancellationToken
            )
            ?? throw new InvalidOperationException($"Товар с вариантом {variantId} не найден на складе {warehouseId}.");

        if (inventory.AvailableForSale < quantity)
        {
            throw new InvalidOperationException(
                $"Недостаточно товара на складе. Доступно к списанию: {inventory.AvailableForSale}, запрошено: {quantity}."
            );
        }

        inventory.AvailableQuantity -= quantity;

        var movement = new StockMovement
        {
            Id = Guid.NewGuid(),
            InventoryId = inventory.Id,
            Type = StockMovementType.WRITE_OFF,
            Quantity = quantity,
            Reason = "Ручное списание со склада",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _context.StockMovements.Add(movement);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<InventoryReservation>> ReserveItemsAsync(
        Guid orderId,
        List<OrderItemRequest> items,
        CancellationToken cancellationToken = default
    )
    {
        if (items is null
            || items.Count == 0)
        {
            throw new ArgumentException(
                "Список позиций для резервирования не может быть пустым.",
                nameof(items)
            );
        }

        var reservations = new List<InventoryReservation>();

        foreach (var item in items)
        {
            var inventory = await _context
                .Inventory
                .Where(
                    x =>
                        x.VariantId == item.VariantId
                        && (x.AvailableQuantity - x.ReservedQuantity) >= item.Quantity
                )
                .OrderByDescending(x => x.AvailableQuantity - x.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            if (inventory is null)
            {
                throw new InvalidOperationException(
                    $"Недостаточно товара для варианта {item.VariantId} в количестве {item.Quantity}."
                );
            }

            inventory.ReservedQuantity += item.Quantity;

            var reservation = new InventoryReservation
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                VariantId = item.VariantId,
                WarehouseId = inventory.WarehouseId,
                Quantity = item.Quantity,
                Status = ReservationStatus.ACTIVE,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            };

            reservations.Add(reservation);
            _context.InventoryReservations.Add(reservation);

            var movement = new StockMovement
            {
                Id = Guid.NewGuid(),
                InventoryId = inventory.Id,
                Type = StockMovementType.RESERVATION,
                Quantity = item.Quantity,
                Reason = $"Резерв под заказ {orderId}",
                ReferenceType = "Order",
                ReferenceId = orderId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            _context.StockMovements.Add(movement);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return reservations;
    }

    public async Task ConfirmReservationAsync(
        Guid orderId,
        CancellationToken cancellationToken = default
    )
    {
        var reservations = await _context
            .InventoryReservations
            .Where(
                r =>
                    r.OrderId == orderId
                    && r.Status == ReservationStatus.ACTIVE
            )
            .ToListAsync(cancellationToken);

        if (reservations.Count == 0)
        {
            return;
        }

        foreach (var reservation in reservations)
        {
            var inventory = await _context.Inventory.FirstOrDefaultAsync(
                x =>
                    x.VariantId == reservation.VariantId
                    && x.WarehouseId == reservation.WarehouseId,
                cancellationToken
            );

            if (inventory is not null)
            {
                inventory.ReservedQuantity = Math.Max(
                    0,
                    inventory.ReservedQuantity - reservation.Quantity
                );
                inventory.AvailableQuantity = Math.Max(
                    0,
                    inventory.AvailableQuantity - reservation.Quantity
                );

                var movement = new StockMovement
                {
                    Id = Guid.NewGuid(),
                    InventoryId = inventory.Id,
                    Type = StockMovementType.SALE,
                    Quantity = reservation.Quantity,
                    Reason = $"Списание и продажа по заказу {orderId}",
                    ReferenceType = "Order",
                    ReferenceId = orderId,
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                _context.StockMovements.Add(movement);
            }

            reservation.Status = ReservationStatus.CONFIRMED;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseReservationAsync(
        Guid orderId,
        CancellationToken cancellationToken = default
    )
    {
        var reservations = await _context
            .InventoryReservations
            .Where(
                r =>
                    r.OrderId == orderId
                    && r.Status == ReservationStatus.ACTIVE
            )
            .ToListAsync(cancellationToken);

        if (reservations.Count == 0)
        {
            return;
        }

        foreach (var reservation in reservations)
        {
            var inventory = await _context.Inventory.FirstOrDefaultAsync(
                x =>
                    x.VariantId == reservation.VariantId
                    && x.WarehouseId == reservation.WarehouseId,
                cancellationToken
            );

            if (inventory is not null)
            {
                inventory.ReservedQuantity = Math.Max(
                    0,
                    inventory.ReservedQuantity - reservation.Quantity
                );

                var movement = new StockMovement
                {
                    Id = Guid.NewGuid(),
                    InventoryId = inventory.Id,
                    Type = StockMovementType.RELEASE,
                    Quantity = reservation.Quantity,
                    Reason = $"Освобождение резерва для заказа {orderId}",
                    ReferenceType = "Order",
                    ReferenceId = orderId,
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                _context.StockMovements.Add(movement);
            }

            reservation.Status = ReservationStatus.RELEASED;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
