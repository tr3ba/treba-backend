using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Product
{
    public enum ProductImageChangeType
    {
        Create,
        Update,
        Delete
    }

    public enum ProductImageChangeStatus
    {
        Pending,
        Approved,
        Rejected
    }

    public sealed class ProductImageChange
    {
        public Guid Id
        {
            get; set;
        }

        public Guid ProductId
        {
            get; set;
        }

        public Guid? ImageId
        {
            get; set;
        }

        public ProductImageChangeType ChangeType
        {
            get; set;
        }

        public ProductImageChangeStatus Status { get; set; } = ProductImageChangeStatus.Pending;

        public Guid? VariantId
        {
            get; set;
        }

        public string? ImageUrl
        {
            get; set;
        }

        public string? AltText
        {
            get; set;
        }

        public int? SortOrder
        {
            get; set;
        }

        public bool? IsMain
        {
            get; set;
        }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt
        {
            get; set;
        }

        public Guid? ReviewedByUserId
        {
            get; set;
        }

        public string? RejectionReason
        {
            get; set;
        }
    }
}
