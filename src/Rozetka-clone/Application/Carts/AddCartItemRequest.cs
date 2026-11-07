using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Carts
{
    public sealed class AddCartItemRequest
    {
        public Guid ProductVariantId
        {
            get; init;
        }

        public int Quantity
        {
            get; init;
        }
    }
}
