using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Abstractions
{
    public interface IApplicationTransaction : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken cancellationToken = default);
    }

    public interface IApplicationTransactionManager
    {
        Task<IApplicationTransaction> BeginAsync(
            CancellationToken cancellationToken = default
        );

        Task<IApplicationTransaction> BeginForProductAsync(
            Guid productId,
            CancellationToken cancellationToken = default
        );
    }

}
