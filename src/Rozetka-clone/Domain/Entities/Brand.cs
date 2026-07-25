namespace Domain.Entities;

public class Brand
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Description { get; set; }
    public string LogoUrl { get; set; }
    public bool IsActive { get; set; }
}