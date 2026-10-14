using Contracts.Orders;

namespace Application.Orders;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);

    Task<OrderDto?> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task CancelOrderAsync(Guid orderId, Guid? userId, string? reason, CancellationToken cancellationToken = default);

    Task PayOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}