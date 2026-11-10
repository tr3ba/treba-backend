using System.Transactions;
using Application.Abstractions;
using Application.Common;
using Application.Inventory;
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
    Application.Delivery.CheckoutDeliveryService? delivery = null,
    Application.Marketplace.CustomerNotificationQueue? notifications = null) : IOrderService
{
    public bool Staff => actor.Role is "Administrator" or "Manager";

    private Guid ActorId => actor.UserId ?? throw new UnauthorizedAccessException();

    internal IQueryable<Order> VisibleOrders()
    {
        if (Staff)
            return db.Orders;

        if (actor.Role == "Seller")
        {
            return db.Orders.Where(order => db.OrderItems.Any(item =>
                item.OrderId == order.Id
                && db.Sellers.Any(seller =>
                    seller.Id == item.SellerId
                    && seller.UserId == actor.UserId
                    && seller.Status == SellerStatus.ACTIVE)));
        }

        return db.Orders.Where(o => o.CustomerId == actor.UserId);
    }

    internal async Task<HashSet<Guid>?> SellerIds(CancellationToken ct)
    {
        if (actor.Role != "Seller")
            return null;

        var ids = await db.Sellers
            .Where(seller => seller.UserId == actor.UserId && seller.Status == SellerStatus.ACTIVE)
            .Select(seller => seller.Id)
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<Contracts.Orders.OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var customerId = ActorId;

        var belongsToAnotherCustomer = request.CustomerId != Guid.Empty && request.CustomerId != customerId;

        if (belongsToAnotherCustomer)
            throw new ArgumentException("An order must belong to the signed-in customer.");

        var hasInvalidItems = request.Items is null
            || request.Items.Count is < 1 or > 100
            || request.Items.Any(i => i is null || i.Quantity is < 1 or > 1000000);

        if (hasInvalidItems)
            throw new ArgumentException("Provide between 1 and 100 items with positive quantities.");

        var hasDuplicateVariants = request.Items.Select(i => i.VariantId).Distinct().Count() != request.Items.Count;

        if (hasDuplicateVariants)
            throw new ArgumentException("Duplicate variants are not allowed.");

        var carrier = request.DeliveryMethod ?? (request.DeliveryQuoteId.HasValue ? "NovaPoshta" : "TrebaCourier");
        var hasInvalidDeliverySelection = carrier is not ("NovaPoshta" or "TrebaCourier")
            || (carrier == "NovaPoshta") != request.DeliveryQuoteId.HasValue;

        if (hasInvalidDeliverySelection)
            throw new ArgumentException("Виберіть спосіб доставки. Для Нової пошти потрібен актуальний розрахунок.");

        ValidateText(request.RecipientName, 150, "Recipient name");
        ValidateText(request.RecipientPhone, 32, "Phone");
        ValidateText(request.RecipientEmail, 150, "Email");

        if (!System.Net.Mail.MailAddress.TryCreate(request.RecipientEmail, out _))
            throw new ArgumentException("A valid email is required.");

        var hasOverlongOrderText = request.CustomerComment?.Length > 4000 || request.CouponCode?.Length > 100;

        if (hasOverlongOrderText)
            throw new ArgumentException("Order text is too long.");

        var a = request.Address ?? throw new ArgumentException("Address is required.");

        ValidateText(a.Country, 100, "Country");
        ValidateText(a.City, 100, "City");
        ValidateText(a.Street, 150, "Street");
        ValidateText(a.Building, 50, "Building");
        ValidateText(a.RecipientName, 150, "Recipient name");
        ValidateText(a.RecipientPhone, 32, "Phone");

        var hasOverlongAddressText = a.Region?.Length > 100
            || a.Apartment?.Length > 50
            || a.PostalCode?.Length > 20;

        if (hasOverlongAddressText)
            throw new ArgumentException("Address text is too long.");

        var ids = request.Items.Select(i => i.VariantId).ToList();

        var rows = await (from v in db.ProductVariants
                          join p in db.Products on v.ProductId equals p.Id
                          join store in db.Stores on p.StoreId equals store.Id
                          join seller in db.Sellers on store.SellerId equals seller.Id
                          where ids.Contains(v.Id)
                              && v.IsActive
                              && v.Price > 0
                              && p.Status == ProductStatus.ACTIVE
                              && store.IsActive
                              && seller.Status == SellerStatus.ACTIVE
                              && db.Categories.Any(c => c.Id == p.CategoryId && c.IsActive)
                              && db.Brands.Any(b => b.Id == p.BrandId && b.IsActive)
                          select new
                          {
                              Variant = v,
                              Product = p,
                              SellerId = seller.Id
                          }).ToListAsync(ct);

        var hasUnavailableItems = rows.Count != ids.Count;

        if (hasUnavailableItems)
            throw new BusinessRuleException("An item is unavailable for purchase.");

        var discount = await discounts.CalculateDiscountAsync(
            customerId,
            request.Items.Select(i => new PriceItem(i.VariantId, i.Quantity)).ToList(),
            request.CouponCode,
            ct);
        var subtotal = request.Items.Sum(i => rows.Single(r => r.Variant.Id == i.VariantId).Variant.Price * i.Quantity);

        var exceedsMaximumOrderAmount = subtotal > 9999999999999999.99m;

        if (exceedsMaximumOrderAmount)
            throw new ArgumentException("The order amount is too large.");

        var pricesChanged = discount.Subtotal != subtotal;

        if (pricesChanged)
            throw new BusinessRuleException("Prices changed. Refresh the order.");

        DeliveryQuote? quote = null;

        if (request.DeliveryQuoteId is { } quoteId)
        {
            var deliveryService = delivery ?? throw new BusinessRuleException("Delivery is not configured.");
            var quoteItems = request.Items.Select(item =>
                new Contracts.Delivery.DeliveryQuoteItem(item.VariantId, item.Quantity));

            quote = await deliveryService.ValidateAsync(
                quoteId,
                customerId,
                quoteItems,
                request.CouponCode,
                subtotal,
                discount.TotalAmount,
                ct);
        }

        var deliveryPrice = discount.FreeDelivery ? 0 : quote?.Price ?? 50;

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
                DeliveryPrice = deliveryPrice,
                TotalAmount = discount.TotalAmount + deliveryPrice,
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

            // Save the destination at checkout; assign its tracking reference only after payment.
            db.OrderShipments.Add(new()
            {
                OrderId = order.Id,
                Carrier = carrier,
                CityRef = quote?.CityRef ?? Guid.Empty,
                PickupPointRef = quote?.PickupPointRef ?? Guid.Empty,
                City = quote?.City ?? a.City,
                PickupPoint = quote?.PickupPoint ?? string.Join(", ", new[] { a.Street, a.Building, a.Apartment }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Postomat = quote?.Postomat ?? false,
                QuotedPrice = deliveryPrice,
                WeightKg = quote?.WeightKg ?? 0,
                EstimatedWeight = quote?.EstimatedWeight ?? false
            });

            if (quote is not null)
            {
                quote.UsedByOrderId = order.Id;
                quote.Version = Guid.NewGuid();
                order.Address.Country = "Україна";
                order.Address.City = quote.City[..Math.Min(100, quote.City.Length)];
                order.Address.Street = quote.PickupPoint[..Math.Min(150, quote.PickupPoint.Length)];
                order.Address.Building = "—";
                order.Address.Apartment = null;
                order.Address.Region = null;
                order.Address.PostalCode = null;
            }

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

                var promotionChanged = promotion.Version != discount.PromotionVersion;
                var couponIsInactive = !coupon.Active;
                var couponIsOutsideItsValidityWindow = coupon.StartAt > now.UtcDateTime || coupon.EndAt < now.UtcDateTime;

                if (promotionChanged || couponIsInactive || couponIsOutsideItsValidityWindow)
                    throw new BusinessRuleException("The coupon changed or its usage limit was reached. Refresh the order.");

                var hasReachedGlobalUsageLimit = coupon.UsageLimit > 0 && coupon.UsedCount >= coupon.UsageLimit;

                if (hasReachedGlobalUsageLimit)
                    throw new BusinessRuleException("The coupon changed or its usage limit was reached. Refresh the order.");

                var hasReachedUserUsageLimit = coupon.UsageLimitPerUser > 0
                    && await db.CouponUsages.CountAsync(u => u.CouponId == couponId && u.UserId == customerId, ct) >= coupon.UsageLimitPerUser;

                if (hasReachedUserUsageLimit)
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

            if (notifications is not null)
                await notifications.OrderAsync(order, orderId, order.Status.ToString(), ct);
            await db.SaveChangesAsync(ct);
            // Receipt of payment remains a separate, explicit operation; manual checkout needs no provider.

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
            query = query.Where(o => o.OrderNumber.ToLower().Contains(term)
                || o.RecipientName.ToLower().Contains(term)
                || (actor.Role != "Seller" && (o.RecipientPhone.Contains(term)
                    || o.RecipientEmail.ToLower().Contains(term))));
        }

        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var items = await db.OrderItems.AsNoTracking().Where(i => ids.Contains(i.OrderId)).ToListAsync(ct);
        var sellers = await SellerIds(ct);

        var rows = orders
            .Select(order => Row(order, items.Where(item => item.OrderId == order.Id), sellers))
            .ToList();
        var pageCount = (int)Math.Ceiling(total / (double)size);

        return new(rows, page, size, total, pageCount);
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
        var shipment = delivery is null ? null : await db.OrderShipments.AsNoTracking().SingleOrDefaultAsync(s => s.OrderId == id, ct);

        var address = a is null || partial
            ? null
            : new OrderAddressView(a.Country, a.Region, a.City, a.Street, a.Building, a.Apartment, a.PostalCode, a.RecipientName, a.RecipientPhone);
        var lines = items.Select(item => new OrderLine(
            item.Id,
            item.ProductId,
            item.VariantId,
            item.SellerId,
            item.ProductName,
            item.VariantName,
            item.Sku,
            item.Quantity,
            item.UnitPrice,
            item.DiscountAmount,
            item.TotalPrice)).ToList();
        var events = order.StatusHistory
            .OrderByDescending(history => history.CreatedAt)
            .ThenBy(history => history.Id)
            .Select(history => new OrderEvent(
                history.PreviousStatus.ToString(),
                history.NewStatus.ToString(),
                actor.Role == "Seller" ? null : history.Comment,
                actor.Role == "Seller" ? null : history.ChangedBy,
                history.CreatedAt))
            .ToList();
        var deliveryView = partial || shipment is null
            ? null
            : Application.Delivery.CheckoutDeliveryService.View(shipment, order.Status.ToString());

        return new OrderDetails(
            Row(order, order.Items, sellers),
            partial ? "" : order.RecipientPhone,
            partial ? "" : order.RecipientEmail,
            partial ? null : order.CustomerComment,
            partial ? items.Sum(item => item.UnitPrice * item.Quantity) : order.Subtotal,
            partial ? items.Sum(item => item.DiscountAmount) : order.DiscountAmount,
            partial ? 0 : order.DeliveryPrice,
            address,
            lines,
            events,
            Convert.ToBase64String(order.Version),
            Allowed(order.Status, partial).Select(status => status.ToString()).ToList(),
            partial,
            deliveryView);
    }

    internal static OrderRow Row(Order order, IEnumerable<OrderItem> all, HashSet<Guid>? sellers = null)
    {
        var items = all.ToList();
        var visible = items.Where(i => sellers is null || sellers.Contains(i.SellerId)).ToList();

        var totalAmount = sellers is null || visible.Count == items.Count
            ? order.TotalAmount
            : visible.Sum(item => item.TotalPrice);
        var itemCount = visible.Sum(item => item.Quantity);

        return new(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.RecipientName,
            order.Status.ToString(),
            totalAmount,
            order.Currency,
            itemCount,
            order.CreatedAt);
    }

    public async Task ChangeStatusAsync(Guid id, ChangeOrderStatusRequest request, CancellationToken ct)
    {
        var order = await VisibleOrders().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new KeyNotFoundException();

        if (Convert.ToBase64String(order.Version) != request.ExpectedVersion)
            throw new BusinessRuleException("The order changed. Reload it before saving.");

        var next = Parse(request.Status);
        var sellers = await SellerIds(ct);
        var sellerHasHiddenItems = sellers is not null && order.Items.Any(item => !sellers.Contains(item.SellerId));
        var nextStatusIsNotAllowed = !Allowed(order.Status, sellerHasHiddenItems).Contains(next);

        if (nextStatusIsNotAllowed)
            throw new BusinessRuleException("This status transition is not allowed for your role.");

        await StageTransitionAsync(order, next, request.Reason, ct);
        await db.SaveChangesAsync(ct);
    }

    internal async Task StageTransitionAsync(Order order, OrderStatus next, string? reason, CancellationToken ct)
    {
        var reasonIsTooLong = reason?.Length > 500;
        var cancellationHasNoReason = next == OrderStatus.CANCELLED && string.IsNullOrWhiteSpace(reason);

        if (reasonIsTooLong || cancellationHasNoReason)
            throw new ArgumentException("A cancellation reason of at most 500 characters is required.");

        var previous = order.Status;

        if (next == OrderStatus.PAID)
        {
            await inventory.StageConfirmAsync(order.Id, ct);
            var shipment = await db.OrderShipments.SingleOrDefaultAsync(s => s.OrderId == order.Id, ct);

            if (shipment is null)
            {
                var address = await db.OrderAddresses.SingleOrDefaultAsync(a => a.OrderId == order.Id, ct);
                shipment = new()
                {
                    OrderId = order.Id,
                    Carrier = "TrebaCourier",
                    City = address?.City ?? "",
                    PickupPoint = address is null ? "" : string.Join(", ", new[] { address.Street, address.Building, address.Apartment }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    QuotedPrice = order.DeliveryPrice
                };
                db.OrderShipments.Add(shipment);
            }
            shipment.Reference ??= "TRB-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            order.PaymentStatus = PaymentStatus.SUCCEEDED;
        }

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

        var eventId = Guid.NewGuid();
        db.OrderStatusHistories.Add(new()
        {
            Id = eventId,
            OrderId = order.Id,
            PreviousStatus = previous,
            NewStatus = next,
            ChangedBy = actor.UserId,
            Comment = reason?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        });

        if (notifications is not null)
            await notifications.OrderAsync(order, eventId, next.ToString(), ct);
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

        if (order is null || order.Status != OrderStatus.AWAITING_PAYMENT)
            return;

        var hasExpiredReservation = await db.InventoryReservations.AnyAsync(r =>
            r.OrderId == id
            && r.Status == ReservationStatus.ACTIVE
            && r.ExpiresAt <= DateTimeOffset.UtcNow, ct);

        if (!hasExpiredReservation)
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
