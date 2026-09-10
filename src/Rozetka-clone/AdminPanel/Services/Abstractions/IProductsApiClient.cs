using AdminPanel.Infrastructure;

namespace AdminPanel.Services.Abstractions;

public interface IProductsApiClient
{
    Task<PagedResult<ProductListItemDto>> GetPendingProductsAsync(int page = 1, int size = 20);
    Task<ApiResponse<bool>> ApproveProductAsync(Guid id);
    Task<ApiResponse<bool>> RejectProductAsync(Guid id, string reason);
}

public record ProductListItemDto(
    Guid Id,
    Guid StoreId,
    Guid CategoryId,
    Guid BrandId,
    string Name,
    string Slug,
    string Status,
    decimal Price,
    double AverageRating,
    int ReviewCount,
    DateTime CreatedAt
);
