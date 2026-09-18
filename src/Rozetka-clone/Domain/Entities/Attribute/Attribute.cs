using Domain.Enums;

namespace Domain.Entities.Attribute;

public class Attribute
{
    public Guid Id { get; private set; }
    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public AttributeType Type { get; private set; }

    public bool IsRequired { get; private set; }
    public bool IsFilterable { get; private set; }
    public bool IsComparable { get; private set; }

    public string? Unit { get; private set; }
    public int SortOrder { get; private set; }

    private Attribute()
    {
    }

    public Attribute(
        Guid id,
        Guid categoryId,
        string name,
        string code,
        AttributeType type,
        bool isRequired,
        bool isFilterable,
        bool isComparable,
        string? unit,
        int sortOrder)
    {
        Id = id;
        CategoryId = categoryId;
        Name = name;
        Code = code;
        Type = type;
        IsRequired = isRequired;
        IsFilterable = isFilterable;
        IsComparable = isComparable;
        Unit = unit;
        SortOrder = sortOrder;
    }

    public void Update(
        string name,
        string code,
        AttributeType type,
        bool isRequired,
        bool isFilterable,
        bool isComparable,
        string? unit,
        int sortOrder)
    {
        Name = name;
        Code = code;
        Type = type;
        IsRequired = isRequired;
        IsFilterable = isFilterable;
        IsComparable = isComparable;
        Unit = unit;
        SortOrder = sortOrder;
    }
}