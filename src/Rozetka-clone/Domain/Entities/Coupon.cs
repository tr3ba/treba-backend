using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Coupon
    {
        public Guid Id { get; set; }

        public Guid PromotionId { get; set; }

        public string Code { get; set; } = string.Empty;

        public int UsageLimit { get; set; }

        public int UsageLimitPerUser { get; set; }

        public int UsedCount { get; set; }

        public bool Active { get; set; }

        public DateTime StartAt { get; set; }

        public DateTime EndAt { get; set; }
    }
}
