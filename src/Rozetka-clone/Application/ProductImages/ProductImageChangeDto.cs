using Domain.Entities.Product;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.ProductImages
{
    public sealed class ProductImageChangeDto
    {
        public Guid Id { get; init; }

        public Guid ProductId { get; init; }

        public Guid? ImageId { get; init; }

        public ProductImageChangeType ChangeType { get; init; }

        public ProductImageChangeStatus Status { get; init; }

        public Guid? VariantId { get; init; }

        public string? ImageUrl { get; init; }

        public string? AltText { get; init; }

        public int? SortOrder { get; init; }

        public bool? IsMain { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? ReviewedAt { get; init; }

        public string? RejectionReason { get; init; }
    }

}
