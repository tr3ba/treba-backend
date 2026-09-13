using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Products
{
    public sealed class UpdateProductRequest
    {
        public Guid? CategoryId { get; init; }
        public Guid? BrandId { get; init; }

        public string? Name { get; init; }
        public string? Slug { get; init; }

        public string? ShortDescription { get; init; }
        public string? Description { get; init; }

        public int? WarrantyMonth { get; init; }

        public string? CountryOfOrigin { get; init; }
    }
}
