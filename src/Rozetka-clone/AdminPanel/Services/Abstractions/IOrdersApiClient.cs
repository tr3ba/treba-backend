using AdminPanel.Infrastructure;

namespace AdminPanel.Services.Abstractions;

public interface IOrdersApiClient
{
    Task<PagedResult<OrderSummaryDto>> GetOrdersAsync(int page = 1, int size = 20, string? status = null);
    Task<ApiResponse<OrderDto>> GetOrderByIdAsync(Guid id);
    Task<ApiResponse<bool>> UpdateOrderStatusAsync(Guid id, string status);
    Task<ApiResponse<bool>> RefundOrderAsync(Guid orderId, decimal amount);
}

public record OrderSummaryDto(
    Guid Id, 
    string OrderNumber, 
    Guid CustomerId, 
    string Status, 
    string PaymentStatus, 
    string DeliveryStatus, 
    decimal TotalAmount, 
    DateTime CreatedAt
);

public record OrderDto(
    Guid Id, 
    string OrderNumber, 
    Guid CustomerId, 
    string Status, 
    string PaymentStatus, 
    string DeliveryStatus, 
    decimal Subtotal, 
    decimal DiscountAmount, 
    decimal DeliveryPrice, 
    decimal TotalAmount, 
    string RecipientName, 
    string RecipientPhone, 
    DateTime CreatedAt
);
