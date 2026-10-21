using System.Transactions;
using Application.Abstractions;
using Application.Common;
using Application.Inventory;
using Application.Payments;
using Application.Promotions;
using Contracts.Admin.Operations;
using Contracts.Common;
using Contracts.Orders;
using Domain.Entities;
using Domain.Entities.Orders;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Orders;

public sealed class OrderService(
    IApplicationDbContext db,
    InventoryService inventory,
    IDiscountService discounts,
    ICurrentActor actor,
    IPaymentService paymentService) : IOrderService
{
    public bool Staff => actor.Role is "Administrator" or "Manager";
    private Guid ActorId => actor.UserId ?? throw new UnauthorizedAccessException();

    internal IQueryable<Order> VisibleOrders()
    {
        if (Staff)
            return db.Orders;
        if (actor.Role == "Seller")
            return db.Orders.Where(o => db.OrderItems.Any(i => i.OrderId == o.Id
            && db.Sellers.Any(s => s.Id == i.SellerId && s.UserId == actor.UserId && s.Status == SellerStatus.ACTIVE)));
        return db.Orders.Where(o => o.CustomerId == actor.UserId);
    }

    internal async Task<HashSet<Guid>?> SellerIds(CancellationToken ct) => actor.Role == "Seller"
        ? (await db.Sellers.Where(s => s.UserId == actor.UserId && s.Status == SellerStatus.ACTIVE).Select(s => s.Id).ToListAsync(ct)).ToHashSet() : null;

    public async Task<Contracts.Orders.OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var customerId = ActorId;
        if (request.CustomerId != Guid.Empty && request.CustomerId != customerId)
            throw new ArgumentException("An order must belong to the signed-in customer.");
        if (request.Items is null || request.Items.Count is < 1 or > 100 || request.Items.Any(i => i is null || i.Quantity is < 1 or > 1000000))
            throw new ArgumentException("Provide between 1 and 100 items with positive quantities.");
        if (request.Items.Select(i => i.VariantId).Distinct().Count() != request.Items.Count)
            throw new ArgumentException("Duplicate variants are not allowed.");

        ValidateText(request.RecipientName, 150, "Recipient name");
        ValidateText(request.RecipientPhone, 32, "Phone");
        ValidateText(request.RecipientEmail, 150, "Email");

        if (!System.Net.Mail.MailAddress.TryCreate(request.RecipientEmail, out _))
            throw new ArgumentException("A valid email is required.");
        if (request.CustomerComment?.Length > 4000 || request.CouponCode?.Length > 100)
            throw new ArgumentException("Order text is too long.");

        var a = request.Address ?? throw new ArgumentException("Address is required.");
        ValidateText(a.Country, 100, "Country");
        ValidateText(a.City, 100, "City");
        ValidateText(a.Street, 150, "Street");
        ValidateText(a.Building, 50, "Building");
        ValidateText(a.RecipientName, 150, "Recipient name");
        ValidateText(a.RecipientPhone, 32, "Phone");

        if (a.Region?.Length > 100 || a.Apartment?.Length > 50 || a.PostalCode?.Length > 20)
            throw new ArgumentException("Address text is too long.");

        var ids = request.Items.Select(i => i.VariantId).ToList();
        var rows = await (from v in db.ProductVariants
                          join p in db.Products on v.ProductId equals p.Id
                          join store in db.Stores on p.StoreId equals store.Id
                          join seller in db.Sellers on store.SellerId equals seller.Id
                          where ids.Contains(v.Id) && v.IsActive && v.Price > 0 && p.Status == ProductStatus.ACTIVE && store.IsActive && seller.Status == SellerStatus.ACTIVE
                              && db.Categories.Any(c => c.Id == p.CategoryId && c.IsActive) && db.Brands.Any(b => b.Id == p.BrandId && b.IsActive)
                          select new
                          {
                              Variant = v,
                              Product = p,
                              SellerId = seller.Id
                          }).ToListAsync(ct);

        if (rows.Count != ids.Count)
            throw new BusinessRuleException("An item is unavailable for purchase.");

        var discount = await discounts.CalculateDiscountAsync(customerId, request.Items.Select(i => new PriceItem(i.VariantId, i.Quantity)).ToList(), request.CouponCode, ct);
        var subtotal = request.Items.Sum(i => rows.Single(r => r.Variant.Id == i.VariantId).Variant.Price * i.Quantity);

        if (subtotal > 9999999999999999.99m)
            throw new ArgumentException("The order amount is too large.");
        if (discount.Subtotal != subtotal)
            throw new BusinessRuleException("Prices changed. Refresh the order.");

        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();
        var orderNumber = $"ORD-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..30];

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        try
        {
            var inventoryItemsRequest = request.Items
                .Select(i => new OrderItemRequest(i.VariantId, i.Quantity))
                .ToList();

            await inventory.StageReserveAsync(orderId, inventoryItemsRequest, ct);

            var order = new Order
            {
                Id = orderId,
                OrderNumber = orderNumber,
                CustomerId = customerId,
                Status = OrderStatus.AWAITING_PAYMENT,
                PaymentStatus = PaymentStatus.PENDING,
                Currency = "UAH",
                Subtotal = subtotal,
                DiscountAmount = discount.DiscountAmount,
                DeliveryPrice = discount.FreeDelivery ? 0 : 50,
                TotalAmount = discount.TotalAmount + (discount.FreeDelivery ? 0 : 50),
                RecipientName = request.RecipientName.Trim(),
                RecipientPhone = request.RecipientPhone.Trim(),
                RecipientEmail = request.RecipientEmail.Trim(),
                CustomerComment = request.CustomerComment?.Trim(),
                CreatedAt = now,
                Address = new OrderAddress
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Country = a.Country,
                    Region = a.Region,
                    City = a.City,
                    Street = a.Street,
                    Building = a.Building,
                    Apartment = a.Apartment,
                    PostalCode = a.PostalCode,
                    RecipientName = a.RecipientName,
                    RecipientPhone = a.RecipientPhone
                }
            };

            foreach (var item in request.Items)
            {
                var row = rows.Single(r => r.Variant.Id == item.VariantId);
                var lineDiscount = discount.LineDiscounts.GetValueOrDefault(item.VariantId);
                order.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = row.Product.Id,
                    VariantId = row.Variant.Id,
                    SellerId = row.SellerId,
                    ProductName = row.Product.Name,
                    VariantName = row.Variant.Name,
                    Sku = row.Variant.Sku,
                    Quantity = item.Quantity,
                    UnitPrice = row.Variant.Price,
                    DiscountAmount = lineDiscount,
                    TotalPrice = row.Variant.Price * item.Quantity - lineDiscount
                });
            }

            if (discount.CouponId is { } couponId)
            {
                var coupon = await db.Coupons.SingleAsync(c => c.Id == couponId, ct);
                var promotion = await db.Promotions.SingleAsync(p => p.Id == coupon.PromotionId, ct);
                if (promotion.Version != discount.PromotionVersion || !coupon.Active || coupon.StartAt > now.UtcDateTime || coupon.EndAt < now.UtcDateTime
                    || coupon.UsageLimit > 0 && coupon.UsedCount >= coupon.UsageLimit
                    || coupon.UsageLimitPerUser > 0 && await db.CouponUsages.CountAsync(u => u.CouponId == couponId && u.UserId == customerId, ct) >= coupon.UsageLimitPerUser)
                    throw new BusinessRuleException("The coupon changed or its usage limit was reached. Refresh the order.");

                coupon.UsedCount++;
                promotion.Version = Guid.NewGuid();
                db.CouponUsages.Add(new()
                {
                    Id = Guid.NewGuid(),
                    CouponId = couponId,
                    UserId = customerId,
                    OrderId = order.Id,
                    UsedAt = now.UtcDateTime
                });
            }

            order.StatusHistory.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                PreviousStatus = OrderStatus.CREATED,
                NewStatus = order.Status,
                ChangedBy = customerId,
                Comment = "Order placed; stock reserved for 30 minutes.",
                CreatedAt = now
            });

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);

            // Create payment for the order
            await paymentService.CreatePaymentAsync(order.Id, customerId, ct);

            scope.Complete();
            return Map(order);
        }
        catch
        {
            // TransactionScope will automatically rollback if not completed
            throw;
        }
    }

    public async Task<PagedResponse<OrderRow>> ListAsync(int page, int size, string? status, string? search,
        DateTimeOffset? from, DateTimeOffset? to, Guid? customerId, CancellationToken ct)
    {
        ValidatePage(page, size);
        if (from > to)
            throw new ArgumentException("Invalid date range.");

        var query = VisibleOrders().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var state = Parse(status);
            query = query.Where(o => o.Status == state);
        }
        if (from.HasValue)
            query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(o => o.CreatedAt < to.Value);
        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(o => o.OrderNumber.ToLower().Contains(term) || o.RecipientName.ToLower().Contains(term)
                || o.RecipientPhone.Contains(term) || o.RecipientEmail.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var items = await db.OrderItems.AsNoTracking().Where(i => ids.Contains(i.OrderId)).ToListAsync(ct);
        var sellers = await SellerIds(ct);

        return new(orders.Select(o => Row(o, items.Where(i => i.OrderId == o.Id), sellers)).ToList(), page, size, total, (int)Math.Ceiling(total / (double)size));
    }

    public async Task<OrderDetails?> DetailsAsync(Guid id, CancellationToken ct)
    {
        var order = await VisibleOrders().AsNoTracking().Include(o => o.Items).Include(o => o.Address).Include(o => o.StatusHistory).SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            return null;

        var sellers = await SellerIds(ct);
        var items = order.Items.Where(i => sellers is null || sellers.Contains(i.SellerId)).ToList();
        var partial = items.Count != order.Items.Count;
        var a = order.Address;

        return new(Row(order, order.Items, sellers), order.RecipientPhone, order.RecipientEmail, partial ? null : order.CustomerComment,
            partial ? items.Sum(i => i.UnitPrice * i.Quantity) : order.Subtotal,
            partial ? items.Sum(i => i.DiscountAmount) : order.DiscountAmount, partial ? 0 : order.DeliveryPrice,
            a is null ? null : new(a.Country, a.Region, a.City, a.Street, a.Building, a.Apartment, a.PostalCode, a.RecipientName, a.RecipientPhone),
            items.Select(i => new OrderLine(i.Id, i.ProductId, i.VariantId, i.SellerId, i.ProductName, i.VariantName, i.Sku, i.Quantity, i.UnitPrice, i.DiscountAmount, i.TotalPrice)).ToList(),
            order.StatusHistory.OrderByDescending(h => h.CreatedAt).ThenBy(h => h.Id).Select(h => new OrderEvent(h.PreviousStatus.ToString(), h.NewStatus.ToString(), actor.Role == "Seller" ? null : h.Comment, actor.Role == "Seller" ? null : h.ChangedBy, h.CreatedAt)).ToList(),
            Convert.ToBase64String(order.Version), Allowed(order.Status, partial).Select(s => s.ToString()).ToList(), partial);
    }

    internal static OrderRow Row(Order order, IEnumerable<OrderItem> all, HashSet<Guid>? sellers = null)
    {
        var items = all.ToList();
        var visible = items.Where(i => sellers is null || sellers.Contains(i.SellerId)).ToList();
        return new(order.Id, order.OrderNumber, order.CustomerId, order.RecipientName, order.Status.ToString(),
            sellers is null || visible.Count == items.Count ? order.TotalAmount : visible.Sum(i => i.TotalPrice), order.Currency, visible.Sum(i => i.Quantity), order.CreatedAt);
    }

    public async Task ChangeStatusAsync(Guid id, ChangeOrderStatusRequest request, CancellationToken ct)
    {
        var order = await VisibleOrders().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new KeyNotFoundException();
        if (Convert.ToBase64String(order.Version) != request.ExpectedVersion)
            throw new BusinessRuleException("The order changed. Reload it before saving.");

        var next = Parse(request.Status);
        var sellers = await SellerIds(ct);
        if (!Allowed(order.Status, sellers is not null && order.Items.Any(i => !sellers.Contains(i.SellerId))).Contains(next))
            throw new BusinessRuleException("This status transition is not allowed for your role.");

        await StageTransitionAsync(order, next, request.Reason, ct);
        await db.SaveChangesAsync(ct);
    }

    internal async Task StageTransitionAsync(Order order, OrderStatus next, string? reason, CancellationToken ct)
    {
        if (reason?.Length > 500 || next == OrderStatus.CANCELLED && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A cancellation reason of at most 500 characters is required.");

        var previous = order.Status;
        if (next == OrderStatus.PAID)
            await inventory.StageConfirmAsync(order.Id, ct);
        if (next is OrderStatus.CANCELLED or OrderStatus.RETURNED)
            await inventory.StageRestoreAsync(order.Id, ct);

        order.Status = next;
        order.Version = Guid.NewGuid().ToByteArray();

        if (next == OrderStatus.PAID)
            order.ConfirmedAt = DateTimeOffset.UtcNow;
        if (next == OrderStatus.CANCELLED)
            order.CancelledAt = DateTimeOffset.UtcNow;
        if (next == OrderStatus.COMPLETED)
            order.CompletedAt = DateTimeOffset.UtcNow;

        db.OrderStatusHistories.Add(new()
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            PreviousStatus = previous,
            NewStatus = next,
            ChangedBy = actor.UserId,
            Comment = reason?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private IReadOnlyList<OrderStatus> Allowed(OrderStatus current, bool partial)
    {
        if (partial)
            return [];
        var states = current switch
        {
            OrderStatus.CREATED or OrderStatus.AWAITING_PAYMENT => new[] { OrderStatus.PAID, OrderStatus.CANCELLED },
            OrderStatus.PAID => [OrderStatus.CONFIRMED, OrderStatus.CANCELLED],
            OrderStatus.CONFIRMED => [OrderStatus.PROCESSING, OrderStatus.CANCELLED],
            OrderStatus.PROCESSING => [OrderStatus.READY_FOR_SHIPMENT, OrderStatus.CANCELLED],
            OrderStatus.READY_FOR_SHIPMENT => [OrderStatus.SHIPPED, OrderStatus.CANCELLED],
            OrderStatus.SHIPPED => [OrderStatus.DELIVERED],
            OrderStatus.DELIVERED => [OrderStatus.COMPLETED],
            _ => Array.Empty<OrderStatus>()
        };
        if (Staff)
            return states;
        if (actor.Role == "Seller")
            return states.Where(s => s is not (OrderStatus.PAID or OrderStatus.CANCELLED or OrderStatus.COMPLETED)).ToArray();
        return current is OrderStatus.CREATED or OrderStatus.AWAITING_PAYMENT ? [OrderStatus.CANCELLED] : [];
    }

    public async Task<Contracts.Orders.OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (actor.Role == "Seller")
            return null;
        var order = await VisibleOrders().AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? null : Map(order);
    }

    public async Task ExpireReservationAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || order.Status != OrderStatus.AWAITING_PAYMENT || !await db.InventoryReservations.AnyAsync(r => r.OrderId == id && r.Status == ReservationStatus.ACTIVE && r.ExpiresAt <= DateTimeOffset.UtcNow, ct))
            return;
        await StageTransitionAsync(order, OrderStatus.CANCELLED, "Stock reservation expired after 30 minutes without payment.", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelOrderAsync(Guid id, Guid? userId, string? reason, CancellationToken ct = default)
    {
        var details = await DetailsAsync(id, ct) ?? throw new KeyNotFoundException();
        await ChangeStatusAsync(id, new("CANCELLED", details.Version, reason ?? "Cancelled by customer"), ct);
    }

    public async Task PayOrderAsync(Guid id, CancellationToken ct = default)
    {
        if (!Staff)
            throw new BusinessRuleException("Only staff can record receipt of payment.");
        var details = await DetailsAsync(id, ct) ?? throw new KeyNotFoundException();
        await ChangeStatusAsync(id, new("PAID", details.Version, "Receipt of payment confirmed manually by staff."), ct);
    }

    private static Contracts.Orders.OrderDto Map(Order o) => new(
        o.Id,
        o.OrderNumber,
        o.CustomerId,
        o.Status.ToString(),
        o.PaymentStatus.ToString(),
        o.Currency,
        o.Subtotal,
        o.DiscountAmount,
        o.DeliveryPrice,
        o.TotalAmount,
        o.RecipientName,
        o.RecipientPhone,
        o.CreatedAt);

    internal static void ValidatePage(int page, int size)
    {
        if (page is < 1 or > 100000 || size is < 1 or > 100)
            throw new ArgumentException("Invalid pagination.");
    }

    internal static void ValidateText(string? value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
            throw new ArgumentException($"{field} is required and must be at most {max} characters.");
    }

    private static OrderStatus Parse(string status) => Enum.TryParse<OrderStatus>(status, out var result) && Enum.IsDefined(result) && result.ToString() == status ? result : throw new ArgumentException("Unknown order status.");
}
