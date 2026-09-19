using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductTags
{
    public sealed class CreateProductTagRequest
    {
        public string Name { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
    }
}
