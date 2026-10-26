using System.ComponentModel.DataAnnotations;

namespace Contracts.Admin.Operations;

public sealed record OrderRow(Guid Id, string OrderNumber, Guid CustomerId, string CustomerName,
    string Status, decimal TotalAmount, string Currency, int ItemCount, DateTimeOffset CreatedAt);
public sealed record OrderLine(Guid Id, Guid ProductId, Guid VariantId, Guid SellerId, string ProductName,
    string VariantName, string Sku, int Quantity, decimal UnitPrice, decimal DiscountAmount, decimal TotalPrice);
public sealed record OrderAddressView(string Country, string? Region, string City, string Street,
    string Building, string? Apartment, string? PostalCode, string RecipientName, string RecipientPhone);
public sealed record OrderEvent(string PreviousStatus, string NewStatus, string? Comment, Guid? ChangedBy, DateTimeOffset CreatedAt);
public sealed record OrderDetails(OrderRow Summary, string RecipientPhone, string RecipientEmail,
    string? CustomerComment, decimal Subtotal, decimal DiscountAmount, decimal DeliveryPrice,
    OrderAddressView? Address, IReadOnlyList<OrderLine> Items, IReadOnlyList<OrderEvent> History,
    string Version, IReadOnlyList<string> AllowedStatuses, bool PartialOrder,
    Contracts.Delivery.OrderDeliveryView? Delivery = null);
public sealed record ChangeOrderStatusRequest([Required] string Status, [Required] string ExpectedVersion,
    [StringLength(500)] string? Reason);

public sealed class PromotionInput
{
    [Required, StringLength(255)] public string Name { get; set; } = "";
    [StringLength(4000)] public string Description { get; set; } = "";
    [Required] public string Type { get; set; } = "PERCENTAGE";
    [Required] public string Status { get; set; } = "DRAFT";
    public DateTimeOffset StartAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset EndAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
    public decimal DiscountValue
    {
        get; set;
    }
    public decimal? MaximumDiscount
    {
        get; set;
    }
    public decimal? MinimumOrderAmount
    {
        get; set;
    }
    public Guid? ProductId
    {
        get; set;
    }
    public Guid? CategoryId
    {
        get; set;
    }
    public Guid? BrandId
    {
        get; set;
    }
    public Guid? SellerId
    {
        get; set;
    }
    public Guid? ExpectedVersion
    {
        get; set;
    }
    [MinLength(1), MaxLength(30)] public List<CouponInput> Coupons { get; set; } = [new()];
}
public sealed class CouponInput
{
    public Guid? Id
    {
        get; set;
    }
    [Required, StringLength(100), RegularExpression(@"^[A-Za-z0-9_-]+$")]
    public string Code { get; set; } = "";
    [Range(0, int.MaxValue)]
    public int UsageLimit
    {
        get; set;
    }
    [Range(0, int.MaxValue)]
    public int UsageLimitPerUser
    {
        get; set;
    }
    public bool Active { get; set; } = true;
}
public sealed record CouponView(Guid Id, string Code, int UsageLimit, int UsageLimitPerUser, int UsedCount, bool Active);
public sealed record PromotionView(Guid Id, string Name, string Description, string Type, string Status,
    DateTimeOffset StartAt, DateTimeOffset EndAt, decimal DiscountValue, decimal? MaximumDiscount,
    decimal? MinimumOrderAmount, Guid? ProductId, Guid? CategoryId, Guid? BrandId, Guid? SellerId,
    Guid Version, IReadOnlyList<CouponView> Coupons);

public sealed class CreateTicketRequest
{
    public Guid? OrderId
    {
        get; set;
    }
    public Guid? ProductId { get; set; }
    public Guid? ClientRequestId { get; set; }
    [Required] public string Kind { get; set; } = "QUESTION";
    [Required, StringLength(200)] public string Subject { get; set; } = "";
    [Required, StringLength(4000)] public string Message { get; set; } = "";
}
public sealed record TicketMessageRequest([Required, StringLength(4000)] string Message, bool Internal = false, Guid? ClientMessageId = null);
public sealed record ReadTicketRequest(Guid MessageId);
public sealed record ChangeTicketStatusRequest([Required] string Status, Guid ExpectedVersion);
public sealed record TicketRow(Guid Id, Guid? OrderId, string? OrderNumber, Guid CustomerId, string CustomerName,
    string Kind, string Subject, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid Version,
    Guid? ProductId = null, string? ProductName = null, string? LastMessage = null, int UnreadCount = 0, string? ProductSlug = null,
    DateTimeOffset? DeletedAt = null);
public sealed record TicketDeletionRequest(Guid ExpectedVersion);
public sealed record TicketCapabilities(bool CanDelete, bool CanRestore);
public sealed record TicketMessageView(Guid Id, string AuthorName, string AuthorRole, string Message, bool Internal, DateTimeOffset CreatedAt);
public sealed record TicketDetails(TicketRow Summary, IReadOnlyList<TicketMessageView> Messages, IReadOnlyList<string> AllowedStatuses, bool HasEarlierMessages = false);
public sealed record TicketMessagePage(IReadOnlyList<TicketMessageView> Messages, bool HasMore);
public sealed record CustomerRow(Guid Id, string Name, string Email, int Orders, decimal OrderedAmount, int Reviews, DateTimeOffset? LastOrderAt);
public sealed record CustomerDetails(CustomerRow Customer, IReadOnlyList<OrderRow> Orders,
    IReadOnlyList<CustomerReview> Reviews, IReadOnlyList<TicketRow> Tickets);
public sealed record CustomerReview(Guid Id, Guid ProductId, string ProductName, int Rating, string? Comment, string Status, DateTime CreatedAt);
public sealed record OperationsMetrics(int Days, int NewOrders, int CancelledOrders, decimal OrderedAmount,
    int OpenTickets, int OverdueTickets, int OutOfStockProducts, int OverdueModeration);
