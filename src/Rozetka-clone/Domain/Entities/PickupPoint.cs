using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class PickupPoint
    {
        public Guid Id { get; set; }

        public string Provider { get; set; } = null!;

        public string ExternalId { get; set; } = null!;

        public string Name { get; set; } = null!;

        public string City { get; set; } = null!;

        public string Address { get; set; } = null!;

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        public bool Active { get; set; } = true;
    }
}
