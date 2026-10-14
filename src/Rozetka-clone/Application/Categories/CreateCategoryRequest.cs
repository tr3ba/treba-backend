using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Application.Categories
{
    public sealed class CreateCategoryRequest
    {
        public Guid? ParentId { get; init; }

        [Required]
        [StringLength(200)]
        public string Name { get; init; } = string.Empty;

        [Required]
        [StringLength(200)]
        [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        public string Slug { get; init; } = string.Empty;

        public string? Description { get; init; }

        public string? ImageUrl { get; init; }

        public bool IsActive { get; init; } = true;

        public int SortOrder { get; init; }
    }
}
