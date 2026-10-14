using System;
using System.Collections.Generic;
using System.Text;
using Domain.Enums;

namespace Application.Attributes
{
    public sealed class UpdateAttributeRequest
    {
        public string? Name { get; init; }
        public string? Code { get; init; }

        public AttributeType? Type { get; init; }

        public bool? IsRequired { get; init; }
        public bool? IsFilterable { get; init; }
        public bool? IsComparable { get; init; }

        public string? Unit { get; init; }

        public int? SortOrder { get; init; }
    }
}
