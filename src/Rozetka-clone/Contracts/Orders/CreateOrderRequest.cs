namespace Contracts.Orders;

public sealed record CreateOrderRequest(
    Guid CustomerId,
    string CustomerComment,
    string RecipientName,
    string RecipientPhone,
    string RecipientEmail,
    CreateOrderAddressDto Address,
    List<CreateOrderItemDto> Items,
    string? CouponCode = null);
