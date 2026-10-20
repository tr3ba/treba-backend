using Application.Catalog;
using Application.Common;
using Contracts.Telegram;
using Domain.Entities.Telegram;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace WebApi.Telegram;

public sealed class SellerImportQueue(ApplicationDbContext db, SellerCategoryAccess access, TimeProvider clock)
{
    public async Task<IReadOnlyList<ImportJobDto>> AddAsync(Guid userId, QueueImportsRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count is < 1 or > 20 || request.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Url) || i.Url.Length > 1500)) throw new ArgumentException("Добавьте от 1 до 20 ссылок.");
        await RequireSeller(userId, ct);
        var items = request.Items.Select(i => (Url: MarketplaceSourceClient.ProductUri(i.Url).AbsoluteUri, i.CategoryId)).Distinct().ToList();
        foreach (var id in items.Where(i => i.CategoryId.HasValue).Select(i => i.CategoryId!.Value).Distinct()) await access.EnsureAllowedAsync(userId, id, ct);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsNpgsql())
        {
            var lockKey = BitConverter.ToInt64(SHA256.HashData(userId.ToByteArray()), 0);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [lockKey], ct);
        }
        var now = clock.GetUtcNow();
        var urls = items.Select(i => i.Url).ToList();
        var previous = await db.SellerImportJobs.Where(j => j.UserId == userId && urls.Contains(j.SourceUrl)
            && (j.Status == "Queued" || j.Status == "Processing" || j.Status == "Ready" && db.ProductImportDrafts.Any(d => d.Id == j.PreviewId && d.SubmittedAt == null && d.ExpiresAt > now))).ToListAsync(ct);
        var reused = previous.Where(j => items.Any(i => i.Url == j.SourceUrl && i.CategoryId == j.CategoryId)).ToList();
        items = items.Where(i => !reused.Any(j => j.SourceUrl == i.Url && j.CategoryId == i.CategoryId)).ToList();
        if (await db.SellerImportJobs.CountAsync(j => j.UserId == userId && (j.Status == "Queued" || j.Status == "Processing" || j.Status == "Ready" && db.ProductImportDrafts.Any(d => d.Id == j.PreviewId && d.SubmittedAt == null && d.ExpiresAt > clock.GetUtcNow())), ct) + items.Count > 50)
            throw new BusinessRuleException("Завершите текущие импорты: одновременно доступно до 50 ссылок.");
        var batch = Guid.NewGuid();
        var jobs = items.Select(i => new SellerImportJob { UserId = userId, BatchId = batch, SourceUrl = i.Url, CategoryId = i.CategoryId, CreatedAt = now, UpdatedAt = now }).ToList();
        db.SellerImportJobs.AddRange(jobs); await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        var ids = jobs.Select(j => j.Id).Concat(reused.Select(j => j.Id)).ToList();
        return await Query(db.SellerImportJobs.AsNoTracking().Where(j => j.UserId == userId && ids.Contains(j.Id))).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<ImportJobDto>> ListAsync(Guid userId, CancellationToken ct) => await Query(db.SellerImportJobs.AsNoTracking().Where(j => j.UserId == userId).OrderByDescending(j => j.CreatedAt).Take(100)).ToListAsync(ct);
    public async Task RetryAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await RequireSeller(userId, ct);
        var job = await db.SellerImportJobs.SingleOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct) ?? throw new KeyNotFoundException();
        var expired = job.PreviewId.HasValue && await db.ProductImportDrafts.AnyAsync(d => d.Id == job.PreviewId && d.SubmittedAt == null && d.ExpiresAt <= clock.GetUtcNow(), ct);
        if (job.Status != "Failed" && !(job.Status == "Ready" && expired)) throw new BusinessRuleException("Этот импорт нельзя повторить сейчас.");
        job.Status = "Queued"; job.Attempts = 0; job.Error = null; job.PreviewId = null; job.LeaseUntil = null; job.UpdatedAt = clock.GetUtcNow(); job.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }
    private async Task RequireSeller(Guid id, CancellationToken ct)
    {
        if (!await db.Sellers.AnyAsync(s => s.UserId == id && s.Status == SellerStatus.ACTIVE, ct)) throw new BusinessRuleException("Импорт доступен активному продавцу.");
        if (!await db.Users.AnyAsync(u => u.Id == id && !u.IsDeleted && u.Status == Domain.Entities.Users.UserStatus.Active && u.Role != null && u.Role.Name == Domain.Entities.Users.Roles.Seller, ct)) throw new BusinessRuleException("Доступ аккаунта продавца изменился.");
    }
    private IQueryable<ImportJobDto> Query(IQueryable<Domain.Entities.Telegram.SellerImportJob> jobs)
    {
        var now = clock.GetUtcNow();
        return jobs.Select(j => new ImportJobDto(j.Id, j.BatchId, j.SourceUrl,
            db.ProductImportDrafts.Any(d => d.Id == j.PreviewId && d.SubmittedAt != null) ? "Submitted"
                : j.Status == "Ready" && db.ProductImportDrafts.Any(d => d.Id == j.PreviewId && d.ExpiresAt <= now) ? "Expired" : j.Status,
            j.Error, j.PreviewId, db.ProductImportDrafts.Where(d => d.Id == j.PreviewId).Select(d => d.ProductId).FirstOrDefault(), j.CreatedAt, j.Attempts));
    }
}
