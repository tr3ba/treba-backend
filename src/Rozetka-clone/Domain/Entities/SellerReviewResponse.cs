using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class SellerReviewResponse
    {
        public Guid Id { get; set; }

        public Guid ReviewId { get; set; }

        public Guid SellerId { get; set; }

        public string Comment { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
