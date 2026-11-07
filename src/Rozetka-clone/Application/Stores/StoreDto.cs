using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Stores
{
    public sealed class StoreDto
    {
        public Guid Id
        {
            get; init;
        }

        public Guid SellerId
        {
            get; init;
        }

        public string Name { get; init; } = string.Empty;

        public string Slug { get; init; } = string.Empty;

        public string? Description
        {
            get; init;
        }

        public string? LogoUrl
        {
            get; init;
        }

        public bool IsActive
        {
            get; init;
        }

        public DateTime CreatedAt
        {
            get; init;
        }

        public DateTime UpdatedAt
        {
            get; init;
        }
    }
}
