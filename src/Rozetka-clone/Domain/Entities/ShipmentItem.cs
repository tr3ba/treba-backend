using Domain.Entities.Orders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class ShipmentItem
    {
        public Guid Id { get; set; }

        public Guid ShipmentId { get; set; }

        public Guid OrderItemId { get; set; }

        public int Quantity { get; set; }

        public Shipment Shipment { get; set; } = null!;

        public OrderItem OrderItem { get; set; } = null!;
    }
}
