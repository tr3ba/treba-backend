using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Stores
{
    public sealed class CreateStoreRequest
    {
        public string Name { get; init; } = string.Empty;

        public string Slug { get; init; } = string.Empty;

        public string? Description
        {
            get; init;
        }

        public string? LogoUrl
        {
            get; init;
        }
    }
}
