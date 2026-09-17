namespace Contracts.Common;

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Content,
    int Page,
    int Size,
    long TotalElements,
    int TotalPages);
