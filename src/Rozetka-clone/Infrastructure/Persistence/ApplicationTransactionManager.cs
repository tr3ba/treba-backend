
using Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Persistence
{
    public sealed class ApplicationTransactionManager(
        ApplicationDbContext dbContext
    ) : IApplicationTransactionManager
    {
        public async Task<IApplicationTransaction> BeginAsync(
            CancellationToken cancellationToken = default
        )
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );

            return new ApplicationTransaction(transaction);
        }

        public async Task<IApplicationTransaction> BeginForProductAsync(
            Guid productId,
            CancellationToken cancellationToken = default
        )
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );

            try
            {
                if (dbContext.Database.IsNpgsql())
                {
                    var key = BitConverter.ToInt64(
                        productId.ToByteArray(),
                        0
                    );

                    await dbContext.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0})",
                        [key],
                        cancellationToken
                    );
                }

                return new ApplicationTransaction(transaction);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }

        private sealed class ApplicationTransaction(
            IDbContextTransaction transaction
        ) : IApplicationTransaction
        {
            public Task CommitAsync(
                CancellationToken cancellationToken = default
            )
            {
                return transaction.CommitAsync(cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return transaction.DisposeAsync();
            }
        }
    }
}
