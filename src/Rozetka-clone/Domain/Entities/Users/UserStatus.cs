using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Users
{
    public enum UserStatus
    {
        PendingActivation = 0,
        Active = 1,
        Blocked = 2,
        Deleted = 3
    }
}
