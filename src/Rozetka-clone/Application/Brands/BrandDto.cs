using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Brands
{
    public sealed class BrandDto
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? LogoUrl { get; init; }
        public bool IsActive { get; init; }
    }
}