using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class WishlistItem
    {
        public Guid Id { get; set; }

        public Guid WishlistId { get; set; }

        public Guid ProductId { get; set; }

        public DateTime AddedAt { get; set; }
    }

}
