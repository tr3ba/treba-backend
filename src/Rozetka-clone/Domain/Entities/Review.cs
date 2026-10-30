using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Review
    {
        public Guid Id { get; set; }

        public Guid ProductId { get; set; }

        public Guid UserId { get; set; }

        public Guid OrderItemId { get; set; }

        public int Rating { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Comment { get; set; } = string.Empty;

        public string? StaffReply { get; set; }
        public DateTime? StaffRepliedAt { get; set; }
        public Guid? StaffRepliedBy { get; set; }

        public ReviewStatus Status { get; set; }

        public bool VerifiedPurchase { get; set; }

        public int HelpfulCount { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
