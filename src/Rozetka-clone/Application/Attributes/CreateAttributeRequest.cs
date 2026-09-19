using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Attributes
{
    public sealed class CreateAttributeRequest
    {
        public Guid CategoryId { get; init; }

        public string Name { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;

        public AttributeType Type { get; init; }

        public bool IsRequired { get; init; }
        public bool IsFilterable { get; init; }
        public bool IsComparable { get; init; }

        public string? Unit { get; init; }

        public int SortOrder { get; init; }
    }
}
