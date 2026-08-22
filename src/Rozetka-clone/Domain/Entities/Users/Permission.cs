using Domain.Entities.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Users
{
    public sealed class Permission : BaseEntity
    {
        private Permission()
        {
        }

        private Permission(
            string code,
            string name,
            string? description)
        {
            Code = NormalizeCode(code);
            Name = NormalizeRequired(name);
            Description = NormalizeOptional(description);
        }

        public string Code { get; private set; } = null!;

        public string Name { get; private set; } = null!;

        public string? Description { get; private set; }

        public static Permission Create(
            string code,
            string name,
            string? description = null)
        {
            return new Permission(
                code,
                name,
                description);
        }

        public void Update(
            string name,
            string? description)
        {
            Name = NormalizeRequired(name);
            Description = NormalizeOptional(description);
        }

        private static string NormalizeCode(string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            return code
                .Trim()
                .ToUpperInvariant();
        }

        private static string NormalizeRequired(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            return value.Trim();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
