using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class DeliveryMethod
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string Code { get; set; } = null!;

        public string? Description { get; set; }

        public decimal BasePrice { get; set; }

        public bool Active { get; set; } = true;

        public ICollection<Shipment> Shipments { get; set; } = new List<Shipment>();
    }
}
