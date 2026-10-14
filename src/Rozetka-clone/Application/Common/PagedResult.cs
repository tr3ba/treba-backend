using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Common
{
    public sealed class PagedResult<T>
    {
        public IReadOnlyCollection<T> Items { get; init; } = Array.Empty<T>();

        public int Page { get; init; }

        public int PageSize { get; init; }

        public int TotalCount { get; init; }

        public int TotalPages =>
            PageSize > 0
                ? (int)Math.Ceiling(TotalCount / (double)PageSize)
                : 0;
    }
}
