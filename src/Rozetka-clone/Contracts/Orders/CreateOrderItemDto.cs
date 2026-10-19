namespace Contracts.Orders;

public sealed record CreateOrderItemDto(
    Guid ProductId,
    Guid VariantId,
    Guid SellerId,
    string ProductName,
    string VariantName,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    string? ProductSnapshot);