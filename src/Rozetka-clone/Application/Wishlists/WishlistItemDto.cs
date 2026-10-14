using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Wishlists
{
    public sealed class WishlistItemDto
    {
        public Guid Id { get; set; }

        public Guid ProductId { get; set; }

        public string ProductName { get; set; } = "";

        public DateTime AddedAt { get; set; }
    }


}
