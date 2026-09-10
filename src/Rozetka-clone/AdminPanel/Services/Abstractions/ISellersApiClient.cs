using AdminPanel.Infrastructure;

namespace AdminPanel.Services.Abstractions;

public interface ISellersApiClient
{
    Task<PagedResult<SellerDto>> GetSellersAsync(int page = 1, int size = 20, string? status = null);
    Task<ApiResponse<SellerDto>> GetSellerByIdAsync(Guid id);
    Task<ApiResponse<bool>> ApproveSellerAsync(Guid id);
    Task<ApiResponse<bool>> SuspendSellerAsync(Guid id);
}

public record SellerDto(
    Guid Id, 
    Guid UserId, 
    string CompanyName, 
    string LegalName, 
    string TaxNumber, 
    string Status, 
    double Rating, 
    decimal CommissionPercent, 
    DateTime CreatedAt, 
    DateTime? VerifiedAt
);
