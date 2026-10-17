using System.Transactions;
using Application.Abstractions;
using Contracts.Payments;
using Domain.Entities.Payments;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Payments;

public sealed class PaymentService : IPaymentService
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentProvider _paymentProvider;

    public PaymentService(IApplicationDbContext context, IPaymentProvider paymentProvider)
    {
        _context = context;
        _paymentProvider = paymentProvider;
    }

    public async Task<PaymentDto> CreatePaymentAsync(Guid orderId, CancellationToken cancellationToken = default)
    {

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        try
        {
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

            var providerResponse = await _paymentProvider.CreatePaymentAsync(
                new Application.Payments.PaymentProviderRequest(
                    orderId,
                    0,
                    "UAH",
                    paymentId.ToString()
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
        catch
        {
            throw;
        }
    }

    public async Task<PaymentStatusDto> GetPaymentStatusAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await _context.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Платеж с ID {paymentId} не найден.");

        return MapToStatusDto(payment);
    }

    public async Task ProcessWebhookAsync(string provider, PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        bool isValid = _paymentProvider.VerifyWebhook(
            request.Data.ToString() ?? string.Empty,
            request.ProviderEventId);

        if (!isValid)
        {
            throw new InvalidOperationException("Недопустимая подпись вебхука");
        }

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.ExternalPaymentId == request.ProviderEventId, cancellationToken)
            ?? throw new InvalidOperationException($"Платеж с внешним ID {request.ProviderEventId} не найден.");

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        try
        {
            var providerStatus = await _paymentProvider.GetStatusAsync(
                payment.ExternalPaymentId!,
                cancellationToken);

            bool statusChanged = false;
            string previousStatus = payment.Status.ToString();

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
                case "cancelled":
                case "expired":
                    if (payment.Status != PaymentStatus.FAILED)
                    {
                        payment.Status = PaymentStatus.FAILED;
                        payment.FailureReason = providerStatus.ErrorMessage;
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

                if (payment.Status == PaymentStatus.SUCCEEDED)
                {
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            scope.Complete();
        }
        catch
        {
            throw;
        }
    }

    public async Task<RefundDto> RefundAsync(Guid orderId, decimal amount, CancellationToken cancellationToken = default)
    {
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        try
        {
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.OrderId == orderId && p.Status == PaymentStatus.SUCCEEDED, cancellationToken)
                ?? throw new InvalidOperationException($"Успешный платеж для заказа {orderId} не найден.");

            if (amount > payment.Amount)
            {
                throw new ArgumentException("Сумма возврата не может превышать сумму платежа");
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

            if (newStatus == PaymentStatus.REFUNDED)
            {
                payment.Status = PaymentStatus.REFUNDED;
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
        catch
        {
            throw;
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
