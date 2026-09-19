using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductAttributeValues
{
    public sealed class CreateProductAttributeValueRequest
    {
        public Guid? VariantId { get; init; }

        public Guid AttributeId { get; init; }
        public Guid? OptionId { get; init; }

        public string? StringValue { get; init; }
        public decimal? NumberValue { get; init; }
        public bool? BoolValue { get; init; }
    }
}
