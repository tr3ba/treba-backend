using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Users
{
    public sealed class UserDomainException : Exception
    {
        public UserDomainException(string message)
            : base(message)
        {
        }
    }
}
