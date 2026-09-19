using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Attributes
{
    public sealed class AttributeOptionDto
    {
        public Guid Id { get; init; }
        public Guid AttributeId { get; init; }

        public string Value { get; init; } = string.Empty;
        public string? DisplayValue { get; init; }

        public int SortOrder { get; init; }
    }
}
