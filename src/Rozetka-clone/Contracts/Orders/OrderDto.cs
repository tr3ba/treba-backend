namespace Contracts.Orders;

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string Status,
    string PaymentStatus,
    // TODO: Remove obsolete fields after implementing Payment and Delivery
    // string DeliveryStatus,
    string Currency,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal DeliveryPrice,
    decimal TotalAmount,
    string RecipientName,
    string RecipientPhone,
    DateTimeOffset CreatedAt);
