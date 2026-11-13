using Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence
{
    public sealed class ShipmentConflictDetector : IShipmentConflictDetector
    {
        public bool IsActiveShipmentConflict(Exception exception)
        {
            return exception is DbUpdateException dbException
                && dbException.InnerException is PostgresException postgresException
                && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
                && postgresException.ConstraintName == "UX_shipments_OrderId_Active";
        }
    }

}
