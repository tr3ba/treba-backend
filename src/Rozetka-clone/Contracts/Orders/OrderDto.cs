namespace Contracts.Orders;

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string Status,
    // TODO: после добавления Payment и Delivery убрать коменты
    // string PaymentStatus,
    // string DeliveryStatus,
    string Currency,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal DeliveryPrice,
    decimal TotalAmount,
    string RecipientName,
    string RecipientPhone,
    DateTimeOffset CreatedAt);