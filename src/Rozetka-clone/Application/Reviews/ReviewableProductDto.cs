namespace Application.Reviews;

public sealed record ReviewableProductDto(Guid OrderItemId, Guid ProductId, string Title,
    string? ProductSlug, string? ImageUrl, Guid OrderId, string OrderNumber);
