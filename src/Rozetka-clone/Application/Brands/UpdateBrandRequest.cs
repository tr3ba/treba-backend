using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Brands
{
    public sealed class UpdateBrandRequest
    {
        public string? Name { get; init; }
        public string? Slug { get; init; }
        public string? Description { get; init; }
        public string? LogoUrl { get; init; }
        public bool? IsActive { get; init; }
    }
}
