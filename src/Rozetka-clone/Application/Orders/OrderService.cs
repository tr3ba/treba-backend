using System.Transactions;
using Application.Abstractions;
using Application.Inventory;
using Application.Payments;
using Contracts.Orders;
using Domain.Entities.Orders;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Orders;

public sealed class OrderService : IOrderService
{
    private readonly IApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;

    public OrderService(IApplicationDbContext context, IInventoryService inventoryService, IPaymentService paymentService)
    {
        _context = context;
        _inventoryService = inventoryService;
        _paymentService = paymentService;
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("Заказ должен содержать хотя бы одну позицию.", nameof(request.Items));
        }

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        try
        {
            var inventoryItemsRequest = request.Items
                .Select(i => new OrderItemRequest(i.VariantId, i.Quantity))
                .ToList();

            var orderId = Guid.NewGuid();

            await _inventoryService.ReserveItemsAsync(orderId, inventoryItemsRequest, cancellationToken);

            decimal subtotal = request.Items.Sum(i => i.UnitPrice * i.Quantity);
            decimal totalDiscount = request.Items.Sum(i => i.DiscountAmount * i.Quantity);
            decimal deliveryPrice = 50.00m;
            decimal totalAmount = subtotal - totalDiscount + deliveryPrice;

            var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";

            var order = new Order
            {
                Id = orderId,
                OrderNumber = orderNumber,
                CustomerId = request.CustomerId,
                Status = OrderStatus.AWAITING_PAYMENT,
                // TODO: после добавления Payment и Delivery убрать коменты
                PaymentStatus = PaymentStatus.PENDING,
                // DeliveryStatus = DeliveryStatus.CREATED,
                Currency = "UAH",
                Subtotal = subtotal,
                DiscountAmount = totalDiscount,
                DeliveryPrice = deliveryPrice,
                TotalAmount = totalAmount,
                CustomerComment = request.CustomerComment,
                RecipientName = request.RecipientName,
                RecipientPhone = request.RecipientPhone,
                RecipientEmail = request.RecipientEmail,
                CreatedAt = DateTimeOffset.UtcNow,
                Address = new OrderAddress
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Country = request.Address.Country,
                    Region = request.Address.Region,
                    City = request.Address.City,
                    Street = request.Address.Street,
                    Building = request.Address.Building,
                    Apartment = request.Address.Apartment,
                    PostalCode = request.Address.PostalCode,
                    RecipientName = request.Address.RecipientName,
                    RecipientPhone = request.Address.RecipientPhone
                },
                Items = request.Items.Select(i => new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    ProductId = i.ProductId,
                    VariantId = i.VariantId,
                    SellerId = i.SellerId,
                    ProductName = i.ProductName,
                    VariantName = i.VariantName,
                    Sku = i.Sku,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    DiscountAmount = i.DiscountAmount,
                    TotalPrice = (i.UnitPrice * i.Quantity) - i.DiscountAmount,
                    ProductSnapshot = i.ProductSnapshot
                }).ToList(),
                StatusHistory = new List<OrderStatusHistory>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        PreviousStatus = OrderStatus.CREATED,
                        NewStatus = OrderStatus.AWAITING_PAYMENT,
                        Comment = "Заказ создан, ожидает оплаты",
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                }
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync(cancellationToken);

            // Create payment for the order
            await _paymentService.CreatePaymentAsync(order.Id, request.CustomerId, cancellationToken);

            scope.Complete(); // Успешно завершаем транзакцию

            return MapToDto(order);
        }
        catch
        {
            // Если произошла ошибка, scope завершится без Complete(), и транзакция автоматически откатится
            throw;
        }
    }

    public async Task<OrderDto?> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        return order is null ? null : MapToDto(order);
    }

    public async Task CancelOrderAsync(Guid orderId, Guid? userId, string? reason, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Заказ с ID {orderId} не найден.");

        if (order.Status == OrderStatus.CANCELLED || order.Status == OrderStatus.COMPLETED)
        {
            throw new InvalidOperationException($"Невозможно отменить заказ в статусе {order.Status}.");
        }

        var previousStatus = order.Status;
        order.Status = OrderStatus.CANCELLED;
        order.CancelledAt = DateTimeOffset.UtcNow;

        await _inventoryService.ReleaseReservationAsync(orderId, cancellationToken);

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            PreviousStatus = previousStatus,
            NewStatus = OrderStatus.CANCELLED,
            Comment = reason ?? "Заказ отменен пользователем или системой",
            ChangedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task PayOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Заказ с ID {orderId} не найден.");

        var successfulPayment = order.Payments.FirstOrDefault(p => p.Status == PaymentStatus.SUCCEEDED);
        if (successfulPayment == null)
        {
            throw new InvalidOperationException("Заказ не может быть marcado как оплаченный, так как нет успешного платежа.");
        }

        var previousStatus = order.Status;
        order.Status = OrderStatus.PAID;
        order.PaymentStatus = PaymentStatus.SUCCEEDED;
        order.ConfirmedAt = DateTimeOffset.UtcNow;

        await _inventoryService.ConfirmReservationAsync(orderId, cancellationToken);

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            PreviousStatus = previousStatus,
            NewStatus = OrderStatus.PAID,
            Comment = "Заказ успешно оплачен",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static OrderDto MapToDto(Order order) => new(
        order.Id,
        order.OrderNumber,
        order.CustomerId,
        order.Status.ToString(),
        order.PaymentStatus.ToString(),
        order.Currency,
        order.Subtotal,
        order.DiscountAmount,
        order.DeliveryPrice,
        order.TotalAmount,
        order.RecipientName,
        order.RecipientPhone,
        order.CreatedAt);
}
