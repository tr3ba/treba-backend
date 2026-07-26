using Domain.Enums;

namespace Domain.Entities.Product;

public class Product
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid CategoryId { get; set; }
    public Guid BrandId { get; set; }
    
    public string Name { get; set; }
    public string Slug { get; set; }
    public string ShortDescription { get; set; }
    public string Description { get; set; }
    public ProductStatus Status { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public int SalesCount { get; set; }
    public int WarrantyMonth { get; set; }
    public string CountryOfOrigin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}