using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Carts
{
    public sealed class UpdateCartItemRequest
    {
        public int Quantity
        {
            get; init;
        }
    }
}
