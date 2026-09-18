using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Attributes
{
    public sealed class UpdateAttributeOptionRequest
    {
        public string? Value { get; init; }
        public string? DisplayValue { get; init; }
        public int? SortOrder { get; init; }
    }
}
