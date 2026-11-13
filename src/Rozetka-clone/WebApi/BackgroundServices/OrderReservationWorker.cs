using Application.Abstractions;
using Application.Orders;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace WebApi.BackgroundServices;

public sealed class OrderReservationWorker(IServiceScopeFactory scopes, ILogger<OrderReservationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var now = DateTimeOffset.UtcNow;
                var ids = await db.Orders.Where(o => o.Status == OrderStatus.AWAITING_PAYMENT
                    && db.InventoryReservations.Any(r => r.OrderId == o.Id && r.Status == ReservationStatus.ACTIVE && r.ExpiresAt <= now))
                    .OrderBy(o => o.CreatedAt).Select(o => o.Id).Take(100).ToListAsync(ct);

                foreach (var id in ids)
                {
                    using var itemScope = scopes.CreateScope();
                    try
                    {
                        await itemScope.ServiceProvider.GetRequiredService<OrderService>().ExpireReservationAsync(id, ct);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // Another instance or payment won the race - skip this order
                        logger.LogDebug("Concurrency conflict expiring reservation for order {OrderId}", id);
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue processing other orders
                        // This prevents one problematic reservation from blocking all others (SEC-11)
                        logger.LogError(ex, "Failed to expire reservation for order {OrderId}", id);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Could not expire order reservations."); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
