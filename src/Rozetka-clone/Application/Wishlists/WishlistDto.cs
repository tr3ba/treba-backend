using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Wishlists
{
    public sealed class WishlistDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = "";

        public bool DefaultWishlist { get; set; }

        public DateTime CreatedAt { get; set; }

        public IReadOnlyCollection<WishlistItemDto> Items { get; set; }
            = Array.Empty<WishlistItemDto>();
    }

}
