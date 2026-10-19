namespace Application.Abstractions;

public sealed record ProductMediaFile(byte[] Bytes, string ContentType);

public interface IProductMediaStorage
{
    Task SaveAsync(Guid productId, string fileName, byte[] bytes, CancellationToken ct);
    Task<ProductMediaFile?> ReadAsync(Guid productId, string fileName, CancellationToken ct);
    Task DeleteAsync(Guid productId, string fileName, CancellationToken ct);
}
