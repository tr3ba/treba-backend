using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Carts
{
    public sealed class CartItemDto
    {
        public Guid Id
        {
            get; init;
        }

        public Guid ProductVariantId
        {
            get; init;
        }

        public Guid ProductId
        {
            get; init;
        }

        public string ProductName { get; init; } = string.Empty;

        public string VariantName { get; init; } = string.Empty;

        public string? ImageUrl
        {
            get; init;
        }

        public decimal Price
        {
            get; init;
        }

        public decimal? OldPrice
        {
            get; init;
        }

        public int Quantity
        {
            get; init;
        }

        public decimal TotalPrice
        {
            get; init;
        }

        public bool IsAvailable
        {
            get; init;
        }
    }
}
