using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Categories
{
    public sealed class CreateCategoryRequest
    {
        public Guid? ParentId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Slug { get; init; } = string.Empty;

        public string? Description { get; init; }

        public string? ImageUrl { get; init; }

        public bool IsActive { get; init; } = true;

        public int SortOrder { get; init; }
    }
}
