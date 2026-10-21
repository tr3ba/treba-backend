using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Promotion
    {
        public Guid Id
        {
            get; set;
        }
        public Guid Version { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public PromotionType Type
        {
            get; set;
        }

        public PromotionStatus Status
        {
            get; set;
        }

        public DateTime StartAt
        {
            get; set;
        }

        public DateTime EndAt
        {
            get; set;
        }

        public int Priority
        {
            get; set;
        }

        public bool Stackable
        {
            get; set;
        }
    }
}
