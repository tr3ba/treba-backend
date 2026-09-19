using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductImages
{
    public sealed class UpdateProductImageRequest
    {
        public Guid? VariantId { get; init; }

        public string? ImageUrl { get; init; }
        public string? AltText { get; init; }

        public int? SortOrder { get; init; }
        public bool? IsMain { get; init; }
    }
}
