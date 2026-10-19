using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class PromotionAction
    {
        public Guid Id { get; set; }

        public Guid PromotionId { get; set; }

        public string DiscountType { get; set; } = string.Empty;

        public decimal DiscountValue { get; set; }

        public decimal? MaximumDiscount { get; set; }
    }
}
