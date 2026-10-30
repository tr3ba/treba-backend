using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Reviews
{
    public sealed class ReviewDto
    {
        public Guid Id { get; init; }

        public Guid ProductId { get; init; }

        public Guid UserId { get; init; }

        public Guid OrderItemId { get; init; }

        public int Rating { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Comment { get; init; } = string.Empty;
        public string? StaffReply { get; init; }
        public DateTime? StaffRepliedAt { get; init; }

        public ReviewStatus Status { get; init; }

        public bool VerifiedPurchase { get; init; }

        public int HelpfulCount { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }
    }
}
