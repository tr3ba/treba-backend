namespace Domain.Entities;

public class ProductAttributeValue
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public Guid AttributeId { get; set; }
    public Guid OptionId { get; set; }
    public string StringValue { get; set; }
    public int NumberValue { get; set; }
    public bool BoolValue { get; set; }
}