using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Abstractions
{
    public interface IShipmentConflictDetector
    {
        bool IsActiveShipmentConflict(Exception exception);
    }
}
