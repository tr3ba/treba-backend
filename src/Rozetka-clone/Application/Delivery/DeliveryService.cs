using Application.Abstractions;
using Contracts.Delivery;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Delivery
{
    public sealed class DeliveryService : IDeliveryService
    {
        private readonly IApplicationDbContext _context;
        private readonly IDeliveryProvider _deliveryProvider;

        private readonly ICurrentActor _currentActor;

        public DeliveryService(
            IApplicationDbContext context,
            IDeliveryProvider deliveryProvider,
            ICurrentActor currentActor)
        {
            _context = context;
            _deliveryProvider = deliveryProvider;
            _currentActor = currentActor;
        }

        public async Task<List<DeliveryOptionDto>> CalculateOptionsAsync(
            DeliveryCalculationRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(request.City))
            {
                throw new ArgumentException(
                    "Город доставки обязателен.",
                    nameof(request)
                );
            }

            if (request.OrderAmount < 0)
            {
                throw new ArgumentException(
                    "Сумма заказа не может быть отрицательной.",
                    nameof(request)
                );
            }

            if (request.TotalQuantity <= 0)
            {
                throw new ArgumentException(
                    "Количество товаров должно быть больше нуля.",
                    nameof(request)
                );
            }

            var deliveryMethods = await _context.DeliveryMethods
                .AsNoTracking()
                .Where(x => x.Active)
                .OrderBy(x => x.BasePrice)
                .ToListAsync(cancellationToken);

            var providerRequest = new DeliveryProviderRequest(
                request.City,
                request.OrderAmount,
                request.TotalQuantity
            );

            var providerPrice = await _deliveryProvider.CalculatePriceAsync(
                providerRequest,
                cancellationToken
            );

            var providerName = _deliveryProvider
                .GetType()
                .Name
                .Replace("DeliveryProvider", "");

            return deliveryMethods
                .Select(method => new DeliveryOptionDto(
                    method.Id,
                    method.Name,
                    method.Code,
                    method.Description,
                    method.BasePrice + providerPrice.Price,
                    providerName
                ))
                .ToList();
        }

        public async Task<ShipmentDto> CreateShipmentAsync(
            Guid orderId,
            Guid deliveryMethodId,
            CancellationToken cancellationToken = default
        )
        {
            if (_currentActor.UserId is not Guid userId)
            {
                throw new UnauthorizedAccessException(
                    "Не удалось определить текущего пользователя."
                );
            }

            var order = await _context.Orders
                .Include(x => x.Address)
                .Include(x => x.Items)
                .FirstOrDefaultAsync(
                    x => x.Id == orderId && x.CustomerId == userId,
                    cancellationToken
                )
                ?? throw new KeyNotFoundException(
                    $"Заказ с ID {orderId} не найден или недоступен текущему пользователю."
                );

            if (order.Status != OrderStatus.READY_FOR_SHIPMENT)
            {
                throw new InvalidOperationException(
                    $"Создать отправление можно только для заказа в статусе {OrderStatus.READY_FOR_SHIPMENT}."
                );
            }

            if (order.Address is null)
            {
                throw new InvalidOperationException(
                    "У заказа отсутствует адрес доставки."
                );
            }

            if (order.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "У заказа отсутствуют позиции."
                );
            }

            var deliveryMethod = await _context.DeliveryMethods
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == deliveryMethodId && x.Active,
                    cancellationToken
                )
                ?? throw new KeyNotFoundException(
                    $"Активный способ доставки с ID {deliveryMethodId} не найден."
                );

            var address = string.Join(
                ", ",
                new[]
                {
                    order.Address.Street,
                    order.Address.Building,
                    order.Address.Apartment
                }.Where(x => !string.IsNullOrWhiteSpace(x))
            );

            var totalQuantity = order.Items.Sum(x => x.Quantity);

            var providerResponse = await _deliveryProvider.CreateShipmentAsync(
                new CreateShipmentRequest(
                    order.Id,
                    deliveryMethod.Id,
                    order.Address.City,
                    address,
                    order.TotalAmount,
                    totalQuantity
                ),
                cancellationToken
            );

            var providerName = _deliveryProvider
                .GetType()
                .Name
                .Replace("DeliveryProvider", "");

            var shipment = new Shipment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                DeliveryMethodId = deliveryMethod.Id,
                Provider = providerName,
                TrackingNumber = providerResponse.TrackingNumber,
                Status = ShipmentStatus.CREATED,
                Price = providerResponse.Price,
                EstimatedDeliveryDate = providerResponse.EstimatedDeliveryDate
            };

            foreach (var orderItem in order.Items)
            {
                shipment.Items.Add(new ShipmentItem
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    OrderItemId = orderItem.Id,
                    Quantity = orderItem.Quantity
                });
            }

            _context.Shipments.Add(shipment);

            await _context.SaveChangesAsync(cancellationToken);

            return MapToDto(shipment);
        }

        public async Task<ShipmentDto> UpdateStatusAsync(
            Guid shipmentId,
            ShipmentStatus status,
            CancellationToken cancellationToken = default
        )
        {
            var shipment = await _context.Shipments
                .FirstOrDefaultAsync(
                    x => x.Id == shipmentId,
                    cancellationToken
                )
                ?? throw new KeyNotFoundException(
                    $"Отправление с ID {shipmentId} не найдено."
                );

            if (shipment.Status == status)
            {
                return MapToDto(shipment);
            }

            if (!IsValidStatusTransition(shipment.Status, status))
            {
                throw new InvalidOperationException(
                    $"Недопустимый переход статуса отправления: {shipment.Status} -> {status}."
                );
            }

            shipment.Status = status;

            if (status == ShipmentStatus.SHIPPED && shipment.ShippedAt is null)
            {
                shipment.ShippedAt = DateTimeOffset.UtcNow;
            }

            if (status == ShipmentStatus.DELIVERED && shipment.DeliveredAt is null)
            {
                shipment.DeliveredAt = DateTimeOffset.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return MapToDto(shipment);
        }

        public async Task<TrackingDto> TrackShipmentAsync(
            Guid shipmentId,
            CancellationToken cancellationToken = default
        )
        {
            if (_currentActor.UserId is not Guid userId)
            {
                throw new UnauthorizedAccessException(
                    "Не удалось определить текущего пользователя."
                );
            }

            var shipment = await _context.Shipments
                .AsNoTracking()
                .Join(
                    _context.Orders.Where(x => x.CustomerId == userId),
                    shipment => shipment.OrderId,
                    order => order.Id,
                    (shipment, order) => shipment
                )
                .FirstOrDefaultAsync(
                    x => x.Id == shipmentId,
                    cancellationToken
                )
                ?? throw new KeyNotFoundException(
                    $"Отправление с ID {shipmentId} не найдено или недоступно текущему пользователю."
                );

            if (string.IsNullOrWhiteSpace(shipment.TrackingNumber))
            {
                throw new InvalidOperationException(
                    "У отправления отсутствует трек-номер."
                );
            }

            return await _deliveryProvider.TrackAsync(
                shipment.TrackingNumber,
                cancellationToken
            );
        }

        public async Task<List<PickupPointDto>> GetPickupPointsAsync(
            string city,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(city))
            {
                throw new ArgumentException(
                    "Город обязателен.",
                    nameof(city)
                );
            }

            var normalizedCity = city.Trim();

            return await _context.PickupPoints
                .AsNoTracking()
                .Where(x => x.Active && x.City == normalizedCity)
                .OrderBy(x => x.Name)
                .Select(x => new PickupPointDto(
                    x.Id,
                    x.Provider,
                    x.ExternalId,
                    x.Name,
                    x.City,
                    x.Address,
                    x.Latitude,
                    x.Longitude,
                    x.Active
                ))
                .ToListAsync(cancellationToken);
        }


        private static bool IsValidStatusTransition(
            ShipmentStatus current,
            ShipmentStatus next)
        {
            return current switch
            {
                ShipmentStatus.CREATED =>
                    next is ShipmentStatus.PREPARING or ShipmentStatus.FAILED,

                ShipmentStatus.PREPARING =>
                    next is ShipmentStatus.READY or ShipmentStatus.FAILED,

                ShipmentStatus.READY =>
                    next is ShipmentStatus.SHIPPED or ShipmentStatus.FAILED,

                ShipmentStatus.SHIPPED =>
                    next is ShipmentStatus.IN_TRANSIT
                        or ShipmentStatus.DELIVERED
                        or ShipmentStatus.FAILED,

                ShipmentStatus.IN_TRANSIT =>
                    next is ShipmentStatus.DELIVERED or ShipmentStatus.FAILED,

                ShipmentStatus.FAILED =>
                    next is ShipmentStatus.RETURNED,

                ShipmentStatus.DELIVERED or ShipmentStatus.RETURNED => false,

                _ => false
            };
        }


        private static ShipmentDto MapToDto(Shipment shipment)
        {
            return new ShipmentDto(
                shipment.Id,
                shipment.OrderId,
                shipment.DeliveryMethodId,
                shipment.Provider,
                shipment.TrackingNumber,
                shipment.Status.ToString(),
                shipment.Price,
                shipment.EstimatedDeliveryDate,
                shipment.ShippedAt,
                shipment.DeliveredAt
            );
        }
    }

}
