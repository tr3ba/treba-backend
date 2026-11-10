using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Delivery;

public sealed class DeliveryQuoteCleanupWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<DeliveryQuoteCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await CleanupAsync(db, clock.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Delivery quote cleanup failed ({ErrorType}).", ex.GetType().Name); }
        }
    }

    public static async Task<int> CleanupAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-1);
        var expired = await db.DeliveryQuotes.Where(q => q.UsedByOrderId == null && q.ExpiresAt < cutoff)
            .OrderBy(q => q.ExpiresAt).Take(1000).ToListAsync(ct);
        db.DeliveryQuotes.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
