using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Sellers
{
    public sealed class CreateSellerRequest
    {
        public Guid UserId
        {
            get; init;
        }

        public string CompanyName { get; init; } = string.Empty;

        public string TaxNumber { get; init; } = string.Empty;

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
