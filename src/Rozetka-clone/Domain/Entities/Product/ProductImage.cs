namespace Domain.Entities.Product;

public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int VariantId { get; set; }
    public string ImageUrl { get; set; }
    public string AltText { get; set; }
    public int SortOrder { get; set; }
    public string MainImageUrl { get; set; }
}