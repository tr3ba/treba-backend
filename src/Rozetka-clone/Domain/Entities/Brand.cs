namespace Domain.Entities;

public class Brand
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? LogoUrl { get; set; }

    public bool IsActive { get; set; }
}