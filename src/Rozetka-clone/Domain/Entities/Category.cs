namespace Domain.Entities;

public class Category
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Description { get; set; }
    public string ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int Level  { get; set; }
}