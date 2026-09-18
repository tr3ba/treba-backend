using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Attributes
{
    public sealed class CreateAttributeOptionRequest
    {
        public string Value { get; init; } = string.Empty;
        public string? DisplayValue { get; init; }
        public int SortOrder { get; init; }
    }
}
