using System.Transactions;
using Application.Abstractions;
using Application.Orders;
using Contracts.Payments;
using Domain.Entities.Payments;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Payments;

public sealed class PaymentService : IPaymentService
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentProvider _paymentProvider;
    private readonly IOrderService _orderService;

    public PaymentService(IApplicationDbContext context, IPaymentProvider paymentProvider, IOrderService orderService)
    {
        _context = context;
        _paymentProvider = paymentProvider;
        _orderService = orderService;
    }

    public async Task<PaymentDto> CreatePaymentAsync(Guid orderId, Guid userId, CancellationToken cancellationToken = default)
    {
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == userId, cancellationToken)
            ?? throw new InvalidOperationException($"Заказ с ID {orderId} не найден или не принадлежит пользователю.");

        // Validate order status allows payment
        if (order.Status != OrderStatus.AWAITING_PAYMENT)
        {
            throw new InvalidOperationException(
                $"Невозможно создать платеж для заказа в статусе '{order.Status}'. " +
                $"Оплата разрешена только для заказов в статусе '{OrderStatus.AWAITING_PAYMENT}'.");
        }

        // Check for existing PENDING payment (idempotency)
        var existingPendingPayment = await _context.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == orderId && p.Status == PaymentStatus.PENDING, cancellationToken);

        if (existingPendingPayment != null)
        {
            // Return existing pending payment instead of creating a new one
            return MapToDto(existingPendingPayment);
        }

        var paymentId = Guid.NewGuid();

        var payment = new Payment
        {
            Id = paymentId,
            OrderId = orderId,
            Provider = _paymentProvider.GetType().Name.Replace("PaymentProvider", ""),
            Status = PaymentStatus.PENDING,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        };

        // Use stable idempotency key based on orderId to prevent duplicate provider requests
        var idempotencyKey = $"order_{orderId}";

        var providerResponse = await _paymentProvider.CreatePaymentAsync(
            new Application.Payments.PaymentProviderRequest(
                orderId,
                order.TotalAmount,
                order.Currency,
                idempotencyKey
            ),
            cancellationToken);

        if (providerResponse.Success)
        {
            payment.ExternalPaymentId = providerResponse.ExternalPaymentId;
            payment.PaymentUrl = providerResponse.PaymentUrl;
            payment.Amount = providerResponse.Amount;
            payment.Currency = providerResponse.Currency;
        }
        else
        {
            payment.Status = PaymentStatus.FAILED;
            payment.FailureReason = providerResponse.ErrorMessage;
        }

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync(cancellationToken);

        scope.Complete();

        return MapToDto(payment);
    }

    public async Task<PaymentStatusDto> GetPaymentStatusAsync(Guid paymentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var payment = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Join(_context.Orders.Where(o => o.CustomerId == userId),
                p => p.OrderId,
                o => o.Id,
                (p, o) => p)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Платеж с ID {paymentId} не найден или не принадлежит пользователю.");

        return MapToStatusDto(payment);
    }

    public async Task ProcessWebhookAsync(string provider, PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        // Validate that the provider from route matches the expected provider
        // (we'll validate against the payment's provider after finding it)

        // Idempotency: check if this webhook event was already processed
        var existingEvent = await _context.PaymentTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ExternalTransactionId == request.ProviderEventId && t.Type == "WEBHOOK_EVENT", cancellationToken);

        if (existingEvent != null)
        {
            // Event already processed, return silently (idempotent)
            return;
        }

        // Extract external payment ID from webhook payload
        string? externalPaymentId = ExtractExternalPaymentId(request.Data);

        if (string.IsNullOrEmpty(externalPaymentId))
        {
            throw new InvalidOperationException("Не удалось извлечь внешний ID платежа из payload вебхука.");
        }

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.ExternalPaymentId == externalPaymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Платеж с внешним ID {externalPaymentId} не найден.");

        // Validate that the provider from route matches the payment's provider
        if (!string.Equals(payment.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Провайдер вебхука '{provider}' не соответствует провайдеру платежа '{payment.Provider}'.");
        }

        bool isValid = _paymentProvider.VerifyWebhook(
            request.Data.ToString() ?? string.Empty,
            request.Signature);

        if (!isValid)
        {
            throw new InvalidOperationException("Недопустимая подпись вебхука");
        }

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        var providerStatus = await _paymentProvider.GetStatusAsync(
            payment.ExternalPaymentId!,
            cancellationToken);

        // Validate provider response amount matches stored payment amount
        if (providerStatus.Amount != payment.Amount)
        {
            throw new InvalidOperationException(
                $"Сумма в ответе провайдера ({providerStatus.Amount}) не соответствует сумме платежа ({payment.Amount}).");
        }

        // Terminal states that should not regress
        var terminalStatuses = new HashSet<PaymentStatus>
        {
            PaymentStatus.SUCCEEDED,
            PaymentStatus.REFUNDED,
            PaymentStatus.PARTIALLY_REFUNDED,
            PaymentStatus.CANCELLED
        };

        // If payment is in terminal state, ignore status changes from webhook
        if (terminalStatuses.Contains(payment.Status))
        {
            // Log or handle silently - webhook for terminal state payment is ignored
            return;
        }

        bool statusChanged = false;

            switch (providerStatus.Status.ToLower())
            {
                case "success":
                case "succeeded":
                case "paid":
                    if (payment.Status != PaymentStatus.SUCCEEDED)
                    {
                        payment.Status = PaymentStatus.SUCCEEDED;
                        payment.PaidAt = DateTimeOffset.UtcNow;
                        statusChanged = true;
                    }
                    break;

                case "failed":
                    if (payment.Status != PaymentStatus.FAILED)
                    {
                        payment.Status = PaymentStatus.FAILED;
                        payment.FailureReason = providerStatus.ErrorMessage;
                        statusChanged = true;
                    }
                    break;

                case "cancelled":
                    if (payment.Status != PaymentStatus.CANCELLED)
                    {
                        payment.Status = PaymentStatus.CANCELLED;
                        payment.FailureReason = providerStatus.ErrorMessage;
                        statusChanged = true;
                    }
                    break;

                case "expired":
                    // Expired payments are treated as failed but with a specific reason
                    if (payment.Status != PaymentStatus.FAILED)
                    {
                        payment.Status = PaymentStatus.FAILED;
                        payment.FailureReason = providerStatus.ErrorMessage ?? "Payment expired";
                        statusChanged = true;
                    }
                    break;

                case "pending":
                case "processing":
                    if (payment.Status != PaymentStatus.PENDING)
                    {
                        payment.Status = PaymentStatus.PENDING;
                        statusChanged = true;
                    }
                    break;
            }

            if (statusChanged)
            {
                var transaction = new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    PaymentId = payment.Id,
                    Type = "PAYMENT",
                    ExternalTransactionId = providerStatus.ExternalTransactionId,
                    Amount = providerStatus.Amount,
                    Status = providerStatus.Status,
                    RequestData = request.Data.ToString(),
                    ResponseData = providerStatus.ErrorMessage,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _context.PaymentTransactions.Add(transaction);

                // Record webhook event for idempotency (prevent duplicate processing)
                var webhookEvent = new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    PaymentId = payment.Id,
                    Type = "WEBHOOK_EVENT",
                    ExternalTransactionId = request.ProviderEventId,
                    Amount = 0,
                    Status = providerStatus.Status,
                    RequestData = request.Data.ToString(),
                    ResponseData = providerStatus.ErrorMessage,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _context.PaymentTransactions.Add(webhookEvent);

                if (payment.Status == PaymentStatus.SUCCEEDED)
                {
                    await _orderService.PayOrderAsync(payment.OrderId, cancellationToken);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            scope.Complete();
    }

    public async Task<RefundDto> RefundAsync(Guid orderId, decimal amount, CancellationToken cancellationToken = default)
    {
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        // Validate refund amount is positive
            if (amount <= 0)
            {
                throw new ArgumentException("Сумма возврата должна быть больше нуля.", nameof(amount));
            }

            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.OrderId == orderId && p.Status == PaymentStatus.SUCCEEDED, cancellationToken)
                ?? throw new InvalidOperationException($"Успешный платеж для заказа {orderId} не найден.");

            // Calculate already refunded amount from successful REFUND transactions
            var alreadyRefunded = await _context.PaymentTransactions
                .Where(t => t.PaymentId == payment.Id
                         && t.Type == "REFUND"
                         && (t.Status == "success" || t.Status == "succeeded" || t.Status == "refunded"))
                .SumAsync(t => t.Amount, cancellationToken);

            if (alreadyRefunded + amount > payment.Amount)
            {
                throw new ArgumentException($"Сумма возврата ({alreadyRefunded + amount}) не может превышать сумму платежа ({payment.Amount}). Уже возвращено: {alreadyRefunded}.");
            }

            var refundId = Guid.NewGuid();

            var providerResponse = await _paymentProvider.RefundAsync(
                new Application.Payments.RefundProviderRequest(
                    payment.ExternalPaymentId!,
                    amount,
                    payment.Currency,
                    refundId.ToString()
                ),
                cancellationToken);

            PaymentStatus newStatus = providerResponse.Success
                ? (amount >= payment.Amount ? PaymentStatus.REFUNDED : PaymentStatus.PARTIALLY_REFUNDED)
                : payment.Status;

            if (providerResponse.Success)
            {
                payment.Status = newStatus;
            }

            var refundTransaction = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                Type = "REFUND",
                ExternalTransactionId = providerResponse.ExternalTransactionId,
                Amount = amount,
                Status = providerResponse.Status,
                RequestData = providerResponse.RequestData,
                ResponseData = providerResponse.ResponseData,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _context.PaymentTransactions.Add(refundTransaction);

            await _context.SaveChangesAsync(cancellationToken);
            scope.Complete();

            return new RefundDto(
                refundId,
                payment.Id,
                amount,
                newStatus.ToString(),
                DateTimeOffset.UtcNow);
    }

    private static string? ExtractExternalPaymentId(object data)
    {
        if (data == null)
            return null;

        try
        {
            // Try to parse as JSON and extract common field names
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try common field names for external payment ID
            string[] possibleFields = { "external_payment_id", "externalPaymentId", "payment_id", "paymentId", "id", "object_id", "objectId" };

            foreach (var field in possibleFields)
            {
                if (root.TryGetProperty(field, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            // If payload is an object with nested data (e.g., Stripe: data.object.id)
            if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (dataElement.TryGetProperty("object", out var objectElement) && objectElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (objectElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return idElement.GetString();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static PaymentDto MapToDto(Payment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Provider,
        payment.ExternalPaymentId,
        payment.Status.ToString(),
        payment.Amount,
        payment.Currency,
        payment.PaymentUrl,
        payment.FailureReason,
        payment.CreatedAt,
        payment.PaidAt,
        payment.ExpiresAt);

    private static PaymentStatusDto MapToStatusDto(Payment payment) => new(
        payment.Id,
        payment.Status.ToString(),
        payment.Amount,
        payment.Currency,
        payment.PaidAt,
        payment.FailureReason);
}
