using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Users
{
    public static class Roles
    {
        public const string Customer = nameof(Customer);
        public const string Seller = nameof(Seller);
        public const string Manager = nameof(Manager);
        public const string Moderator = nameof(Moderator);
        public const string Administrator = nameof(Administrator);
    }
}
