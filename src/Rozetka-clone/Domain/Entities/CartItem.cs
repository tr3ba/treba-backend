using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class CartItem
    {
        public Guid Id
        {
            get; set;
        }

        public Guid CartId
        {
            get; set;
        }

        public Guid ProductVariantId
        {
            get; set;
        }

        public int Quantity
        {
            get; set;
        }

        public DateTime CreatedAt
        {
            get; set;
        }

        public DateTime UpdatedAt
        {
            get; set;
        }
    }
}
