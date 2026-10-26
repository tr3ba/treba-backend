using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Shipment
    {
        public Guid Id { get; set; }

        public Guid OrderId { get; set; }

        public Guid DeliveryMethodId { get; set; }

        public string Provider { get; set; } = null!;

        public string? TrackingNumber { get; set; }

        public ShipmentStatus Status { get; set; } = ShipmentStatus.CREATED;

        public decimal Price { get; set; }

        public DateTimeOffset? EstimatedDeliveryDate { get; set; }

        public DateTimeOffset? ShippedAt { get; set; }

        public DateTimeOffset? DeliveredAt { get; set; }

        public DeliveryMethod DeliveryMethod { get; set; } = null!;

        public ICollection<ShipmentItem> Items { get; set; } = new List<ShipmentItem>();
    }
}
