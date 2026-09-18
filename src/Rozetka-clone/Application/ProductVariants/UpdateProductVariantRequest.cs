using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductVariants
{
    public sealed class UpdateProductVariantRequest
    {
        public string? Sku { get; init; }
        public string? Barcode { get; init; }
        public string? Name { get; init; }

        public decimal? Price { get; init; }
        public decimal? OldPrice { get; init; }
        public decimal? CostPrice { get; init; }

        public double? Weight { get; init; }
        public double? Length { get; init; }
        public double? Width { get; init; }
        public double? Height { get; init; }

        public bool? IsActive { get; init; }
    }
}
