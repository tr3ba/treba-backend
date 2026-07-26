namespace Domain.Entities.Attribute;

public class AttributeOption
{
    public Guid Id { get; set; }
    public Guid AttributeId { get; set; }
    public string Value { get; set; }
    public string DisplayValue { get; set; }
    public int SortOrder { get; set; }
}