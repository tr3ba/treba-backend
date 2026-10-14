using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Wishlist
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string Name { get; set; } = "";

        public bool DefaultWishlist { get; set; }

        public DateTime CreatedAt { get; set; }
    }

}
