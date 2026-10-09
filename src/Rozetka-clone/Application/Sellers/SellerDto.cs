using System;
using System.Collections.Generic;
using System.Text;
using Domain.Enums;

namespace Application.Sellers
{
    public sealed class SellerDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }

        public string CompanyName { get; init; } = string.Empty;
        public string TaxNumber { get; init; } = string.Empty;

        public string? Description { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }

        public SellerStatus Status { get; init; }

        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
