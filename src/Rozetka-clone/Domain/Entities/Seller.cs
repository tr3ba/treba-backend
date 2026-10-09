using System;
using System.Collections.Generic;
using System.Text;
using Domain.Enums;

namespace Domain.Entities
{
    public class Seller
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        public string CompanyName { get; set; } = string.Empty;
        public string TaxNumber { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }

        public SellerStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
