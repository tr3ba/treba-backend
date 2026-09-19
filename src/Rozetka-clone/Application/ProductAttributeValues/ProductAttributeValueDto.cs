using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductAttributeValues
{
    public sealed class ProductAttributeValueDto
    {
        public Guid Id { get; init; }
        public Guid ProductId { get; init; }
        public Guid? VariantId { get; init; }

        public Guid AttributeId { get; init; }
        public Guid? OptionId { get; init; }

        public string? StringValue { get; init; }
        public decimal? NumberValue { get; init; }
        public bool? BoolValue { get; init; }
    }
}
