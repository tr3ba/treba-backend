using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Products
{
    public sealed class CreateProductRequest
    {
        public Guid StoreId { get; init; }
        public Guid CategoryId { get; init; }
        public Guid BrandId { get; init; }

        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;

        public string ShortDescription { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        public int WarrantyMonth { get; init; }

        public string CountryOfOrigin { get; init; } = string.Empty;
    }
}
