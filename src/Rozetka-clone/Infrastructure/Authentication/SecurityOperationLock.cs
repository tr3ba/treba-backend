using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Authentication;

internal sealed class SecurityOperationLock : IAsyncDisposable
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1)).ToArray();
    private readonly SemaphoreSlim gate;
    private readonly IDbContextTransaction? transaction;
    private SecurityOperationLock(SemaphoreSlim gate, IDbContextTransaction? transaction)
    {
        this.gate = gate;
        this.transaction = transaction;
    }

    public static async Task<SecurityOperationLock> AcquireAsync(IApplicationDbContext db, string key, CancellationToken ct)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var gate = Gates[digest[0]];
        await gate.WaitAsync(ct);
        IDbContextTransaction? transaction = null;
        try
        {
            if (db is DbContext context && context.Database.IsRelational())
            {
                if (context.Database.CurrentTransaction is null)
                    transaction = await context.Database.BeginTransactionAsync(ct);
                if (context.Database.IsNpgsql())
                    await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [BitConverter.ToInt64(digest, 0)], ct);
            }
            return new(gate, transaction);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            gate.Release();
            throw;
        }
    }

    public Task CommitAsync(CancellationToken ct) => transaction?.CommitAsync(ct) ?? Task.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        try { if (transaction is not null) await transaction.DisposeAsync(); }
        finally { gate.Release(); }
    }
}
