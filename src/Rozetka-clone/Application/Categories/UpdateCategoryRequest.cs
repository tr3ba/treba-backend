using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Application.Categories
{
    public sealed class UpdateCategoryRequest
    {
        [StringLength(200)]
        public string? Name { get; init; }

        [StringLength(200)]
        [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        public string? Slug { get; init; }

        public string? Description { get; init; }

        public string? ImageUrl { get; init; }

        public bool? IsActive { get; init; }

        public int? SortOrder { get; init; }
    }
}
