namespace Domain.Entities.Product;

public class ProductVariant
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }          
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public decimal? OldPrice { get; private set; }
    public decimal? CostPrice { get; private set; }
    public double? Weight { get; private set; }                
    public double? Length { get; private set; }                 
    public double? Width { get; private set; }                
    public double? Height { get; private set; }              
    public bool IsActive { get; private set; }
}