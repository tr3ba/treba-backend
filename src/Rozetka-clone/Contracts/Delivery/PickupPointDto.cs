namespace Contracts.Delivery
{
    public sealed record PickupPointDto(
        Guid Id,
        string Provider,
        string ExternalId,
        string Name,
        string City,
        string Address,
        decimal? Latitude,
        decimal? Longitude,
        bool Active
    );
}
