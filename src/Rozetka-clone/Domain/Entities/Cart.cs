using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Cart
    {
        public Guid Id
        {
            get; set;
        }

        public Guid UserId
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
