using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Delivery
{
    public sealed class ShipmentConflictException : InvalidOperationException
    {
        public ShipmentConflictException(string message)
            : base(message)
        {
        }

        public ShipmentConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

}
