using System.ComponentModel.DataAnnotations;

namespace Contracts.Marketplace;

public sealed class SellerApplicationInput
{
    [Required, StringLength(250, MinimumLength = 2)] public string CompanyName { get; set; } = "";
    [Required, StringLength(200, MinimumLength = 2)] public string StoreName { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 2)] public string TaxNumber { get; set; } = "";
    [StringLength(1000)] public string? Website { get; set; }
    [Required, StringLength(200, MinimumLength = 2)] public string ContactName { get; set; } = "";
    [Required, StringLength(150)] public string Position { get; set; } = "";
    [Required, EmailAddress, StringLength(320)] public string Email { get; set; } = "";
    [Required, Phone, StringLength(50)] public string Phone { get; set; } = "";
    public bool IsNonResident { get; set; }
}

public sealed record SellerApplicationView(Guid Id, string CompanyName, string StoreName, string TaxNumber,
    string? Website, string ContactName, string Position, string Email, string Phone, bool IsNonResident,
    string Status, string? ReviewNote, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid Version, Guid? SellerId);
public sealed record ReviewSellerApplicationRequest(Guid Version, bool Approve,
    [StringLength(2000)] string? Note, [Required, RegularExpression("^[a-z0-9][a-z0-9-]{2,99}$")] string StoreSlug,
    bool AllCategories = true, Guid[]? CategoryIds = null);
public sealed record CustomerPreferencesView(Dictionary<string, string> Values, Guid Version);
public sealed record SaveCustomerPreferencesRequest(Dictionary<string, string> Values, Guid Version);
public sealed record CustomerOrderItem(Guid Id, Guid ProductId, string? ProductSlug, string Name, int Quantity,
    decimal UnitPrice, decimal TotalPrice, string? ImageUrl);
public sealed record CustomerOrderEvent(string Status, DateTimeOffset CreatedAt);
public sealed record CustomerOrderView(Guid Id, string OrderNumber, string Status, string PaymentStatus,
    decimal Subtotal, decimal DiscountAmount, decimal DeliveryPrice, decimal TotalAmount, string Currency,
    DateTimeOffset CreatedAt, string RecipientName, string RecipientPhone, string RecipientEmail,
    string? DeliveryAddress, IReadOnlyList<CustomerOrderItem> Items, IReadOnlyList<CustomerOrderEvent> History);
public sealed record MarketplaceNotification(string Key, string Kind, string Title, string Text, string Scope,
    string Path, DateTimeOffset CreatedAt, bool Read = false);
public sealed record NotificationFeed(IReadOnlyList<MarketplaceNotification> Items, int UnreadCount);
public sealed record ReadNotificationsRequest([MaxLength(300)] string[] Keys);
public sealed record CatalogFacetOption(string Value, string Label);
public sealed record CatalogFacet(Guid Id, string Name, IReadOnlyList<CatalogFacetOption> Options);
public sealed record PublicCatalogFacets(IReadOnlyList<Contracts.Catalog.CatalogLookupDto> Brands,
    IReadOnlyList<CatalogFacet> Attributes);
