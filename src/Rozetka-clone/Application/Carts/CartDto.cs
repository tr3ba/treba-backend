using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Carts
{
    public sealed class CartDto
    {
        public Guid Id { get; init; }

        public Guid UserId { get; init; }

        public IReadOnlyList<CartItemDto> Items { get; init; } = Array.Empty<CartItemDto>();

        public int TotalQuantity { get; init; }

        public decimal TotalPrice { get; init; }
    }
}
