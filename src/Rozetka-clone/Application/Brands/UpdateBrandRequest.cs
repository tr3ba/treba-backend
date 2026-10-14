using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Application.Brands
{
    public sealed class UpdateBrandRequest
    {
        [StringLength(200)]
        public string? Name { get; init; }

        [StringLength(200)]
        [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        public string? Slug { get; init; }
        public string? Description { get; init; }
        public string? LogoUrl { get; init; }
        public bool? IsActive { get; init; }
    }
}
