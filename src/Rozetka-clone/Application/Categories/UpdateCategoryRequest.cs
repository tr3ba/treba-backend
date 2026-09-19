using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Categories
{
    public sealed class UpdateCategoryRequest
    {
        public string? Name { get; init; }

        public string? Slug { get; init; }

        public string? Description { get; init; }

        public string? ImageUrl { get; init; }

        public bool? IsActive { get; init; }

        public int? SortOrder { get; init; }
    }
}
