using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Sellers
{
    public sealed class UpdateSellerRequest
    {
        public string? CompanyName
        {
            get; init;
        }

        public string? TaxNumber
        {
            get; init;
        }

        public string? Description
        {
            get; init;
        }

        public string? Phone
        {
            get; init;
        }

        public string? Email
        {
            get; init;
        }
    }
}
