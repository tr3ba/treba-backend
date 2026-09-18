using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Brands
{
    public sealed class CreateBrandRequest
    {
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? LogoUrl { get; init; }
        public bool IsActive { get; init; } = true;
    }
}
