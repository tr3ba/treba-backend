using Application.Common;
using Domain.Entities.Users;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace WebApi.Telegram;

public sealed class SellerWorkspaceWorker(IServiceScopeFactory scopes, IOptions<TelegramOptions> options, ILogger<SellerWorkspaceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkspaceJobsEnabled) return;
        using (var scope = scopes.CreateScope())
            if (!scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.IsNpgsql()) return;
        await Task.WhenAll(Loop(false, stoppingToken), Loop(false, stoppingToken), Loop(true, stoppingToken));
    }
    private async Task Loop(bool notification, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<SellerWorkspaceRunner>();
                var worked = notification ? options.Value.Configured && await runner.DeliverOneAsync(ct) : await runner.ImportOneAsync(ct);
                if (!worked) await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Seller workspace worker paused after {ErrorType}.", ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}

public sealed class SellerWorkspaceRunner(ApplicationDbContext db, ProductImportService importer, ITelegramBotClient bot,
    IOptions<TelegramOptions> options, TimeProvider clock)
{
    public async Task<bool> ImportOneAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var job = await db.SellerImportJobs.Where(j => j.Status == "Queued" || j.Status == "Processing" && j.LeaseUntil < now).OrderBy(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        if (job is null) return false;
        job.Status = "Processing"; job.Attempts++; job.LeaseUntil = now.AddMinutes(3); job.UpdatedAt = now; job.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return false; }
        var leaseVersion = job.Version;
        try
        {
            if (job.Attempts > 3) throw new BusinessRuleException("Импорт был прерван несколько раз. Повторите загрузку вручную.");
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            var preview = await importer.PreviewAsync(job.UserId, job.SourceUrl, ct, job.CategoryId);
            job.PreviewId = preview.Id; job.Status = "Ready"; job.Error = null; job.LeaseUntil = null; job.UpdatedAt = clock.GetUtcNow(); job.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            var current = await db.SellerImportJobs.SingleOrDefaultAsync(j => j.Id == job.Id && j.Version == leaseVersion, ct);
            if (current is null) return true;
            current.Status = "Failed"; current.Error = ex is BusinessRuleException or ArgumentException ? ex.Message[..Math.Min(1900, ex.Message.Length)] : "Не удалось завершить импорт. Повторите загрузку.";
            current.LeaseUntil = null; current.UpdatedAt = clock.GetUtcNow(); current.Version = Guid.NewGuid();
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { }
        }
        return true;
    }

    public async Task<bool> DeliverOneAsync(CancellationToken ct)
    {
        if (!options.Value.Configured) return false;
        var now = clock.GetUtcNow();
        var item = await db.TelegramNotifications.Where(n => n.CompletedAt == null && n.AvailableAt <= now && (n.LeaseUntil == null || n.LeaseUntil < now))
            .OrderBy(n => n.CreatedAt).FirstOrDefaultAsync(ct);
        if (item is null) return false;
        item.LeaseUntil = now.AddMinutes(2); item.Attempts++; item.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return false; }
        var user = await db.Users.AsNoTracking().Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == item.UserId, ct);
        var binding = await db.TelegramBindings.AsNoTracking().SingleOrDefaultAsync(b => b.UserId == item.UserId, ct);
        var productStatus = await db.Products.Where(p => p.Id == item.ProductId).Select(p => (Domain.Enums.ProductStatus?)p.Status).SingleOrDefaultAsync(ct);
        var current = productStatus?.ToString() == item.ExpectedStatus;
        if (item.ExpectedRole == Roles.Seller) current = current && await db.Products.AnyAsync(p => p.Id == item.ProductId
            && db.Stores.Any(s => s.Id == p.StoreId && db.Sellers.Any(seller => seller.Id == s.SellerId && seller.UserId == item.UserId && seller.Status == Domain.Enums.SellerStatus.ACTIVE)), ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active || user.Role?.Name != item.ExpectedRole || binding is null || !current)
        { item.CompletedAt = now; item.Result = "Skipped: access or product status changed"; }
        else
        {
            try
            {
                var url = options.Value.MiniAppUrl + (options.Value.MiniAppUrl.Contains('?') ? "&" : "?") + "view=" + item.View + "&id=" + item.ProductId;
                using var response = await bot.CallAsync("sendMessage", new { chat_id = binding.ChatId, text = item.Text,
                    reply_markup = new { inline_keyboard = new[] { new[] { new { text = "Открыть товар", web_app = new { url } } } } } }, ct);
                item.CompletedAt = clock.GetUtcNow(); item.Result = "Sent";
            }
            catch (TelegramBotException ex)
            {
                if (item.Attempts >= 5 || ex.HttpStatus is 400 or 403) { item.CompletedAt = clock.GetUtcNow(); item.Result = "Delivery unavailable"; }
                else { item.AvailableAt = clock.GetUtcNow().AddSeconds(Math.Min(1800, 30 * Math.Pow(2, item.Attempts))); item.Result = "Waiting to retry"; }
            }
        }
        item.LeaseUntil = null; item.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { }
        return true;
    }
}
