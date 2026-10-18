using Application.Abstractions;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace WebApi.BackgroundServices;

public sealed class PromotionActivationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PromotionActivationWorker> _logger;

    public PromotionActivationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PromotionActivationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdatePromotionStatusesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An error occurred while updating promotion statuses.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task UpdatePromotionStatusesAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<IApplicationDbContext>();

        var now = DateTime.UtcNow;

        var promotionsToActivate = await dbContext.Promotions
            .Where(x =>
                x.Status == PromotionStatus.DRAFT &&
                x.StartAt <= now &&
                x.EndAt > now)
            .ToListAsync(cancellationToken);

        var promotionsToFinish = await dbContext.Promotions
            .Where(x =>
                x.Status == PromotionStatus.ACTIVE &&
                x.EndAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var promotion in promotionsToActivate)
        {
            promotion.Status = PromotionStatus.ACTIVE;
        }

        foreach (var promotion in promotionsToFinish)
        {
            promotion.Status = PromotionStatus.FINISHED;
        }

        if (promotionsToActivate.Count == 0 &&
            promotionsToFinish.Count == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Promotion statuses updated. Activated: {ActivatedCount}, finished: {FinishedCount}.",
            promotionsToActivate.Count,
            promotionsToFinish.Count);
    }
}
