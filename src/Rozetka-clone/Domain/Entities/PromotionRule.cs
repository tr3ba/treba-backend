using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class PromotionRule
    {
        public Guid Id { get; set; }

        public Guid PromotionId { get; set; }

        public PromotionRuleType RuleType { get; set; }

        public string RuleValue { get; set; } = string.Empty;
    }
}
