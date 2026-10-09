namespace AdminPanel.Services.Abstractions;

public interface ISellersApiClient
{
    Task<IReadOnlyList<SellerDto>> GetSellersAsync();
    Task<SellerDto?> GetSellerByIdAsync(Guid id);
}

public sealed record SellerDto(
    Guid Id,
    Guid UserId,
    string CompanyName,
    string TaxNumber,
    string? Description,
    string? Phone,
    string? Email,
    int Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);
