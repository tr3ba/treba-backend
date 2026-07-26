namespace Domain.Entities.ProductTag;

public class ProductTagRelation
{
    public Guid ProductId { get; set; }
    public Guid TagId { get; set; }
}