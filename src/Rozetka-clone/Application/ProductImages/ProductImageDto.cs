using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductImages
{
    public sealed class ProductImageDto
    {
        public Guid Id { get; init; }
        public Guid ProductId { get; init; }
        public Guid? VariantId { get; init; }

        public string ImageUrl { get; init; } = string.Empty;
        public string? AltText { get; init; }

        public int SortOrder { get; init; }
        public bool IsMain { get; init; }
    }
}
