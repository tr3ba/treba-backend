namespace Contracts.Orders;

public sealed record CreateOrderAddressDto(
    string Country,
    string? Region,
    string City,
    string Street,
    string Building,
    string? Apartment,
    string? PostalCode,
    string RecipientName,
    string RecipientPhone);