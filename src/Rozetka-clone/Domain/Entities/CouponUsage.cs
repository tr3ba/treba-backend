using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class CouponUsage
    {
        public Guid Id { get; set; }

        public Guid CouponId { get; set; }

        public Guid UserId { get; set; }

        public Guid OrderId { get; set; }

        public DateTime UsedAt { get; set; }
    }
}
