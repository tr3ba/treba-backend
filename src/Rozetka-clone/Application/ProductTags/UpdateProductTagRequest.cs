using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductTags
{
    public sealed class UpdateProductTagRequest
    {
        public string? Name { get; init; }
        public string? Slug { get; init; }
    }
}
