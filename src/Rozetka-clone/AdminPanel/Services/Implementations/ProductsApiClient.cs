using AdminPanel.Infrastructure;
using AdminPanel.Services.Abstractions;

namespace AdminPanel.Services.Implementations;

public class ProductsApiClient(CatalogApiClient catalog) : IProductsApiClient
{
    public async Task<PagedResult<ProductListItemDto>> GetPendingProductsAsync(int page = 1, int size = 20)
    {
        var result = await catalog.List(page, status: "PENDING_MODERATION");
        return new() { Page=result.Page, Size=result.Size, TotalElements=result.TotalElements, TotalPages=result.TotalPages,
            Content=result.Content.Select(p => new ProductListItemDto(p.Id,p.StoreId,p.CategoryId,p.BrandId,p.Name,p.Slug,p.Status,p.Price,0,0,p.CreatedAt)).ToList() };
    }
    public async Task<ApiResponse<bool>> ApproveProductAsync(Guid id) { await catalog.Action(id,"approve"); return new() { Data=true }; }
    public async Task<ApiResponse<bool>> RejectProductAsync(Guid id, string reason) { await catalog.Action(id,"reject"); return new() { Data=true }; }
}
