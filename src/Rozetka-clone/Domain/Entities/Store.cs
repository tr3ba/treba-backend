using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Store
    {
        public Guid Id
        {
            get; set;
        }

        public Guid SellerId
        {
            get; set;
        }

        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string? Description
        {
            get; set;
        }

        public string? LogoUrl
        {
            get; set;
        }

        public bool IsActive
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
