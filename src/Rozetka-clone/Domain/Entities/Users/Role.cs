using Domain.Entities.Common;
using System;
using System.Collections.Generic;
using System.Security;
using System.Text;

namespace Domain.Entities.Users
{
    public sealed class Role : BaseEntity
    {
        private readonly List<Permission> _permissions = [];

        private Role()
        {
        }

        private Role(
            string name,
            string? description)
        {
            Name = NormalizeRequired(name);
            Description = NormalizeOptional(description);
        }

        public string Name { get; private set; } = null!;

        public string? Description { get; private set; }

        public IReadOnlyCollection<Permission> Permissions =>
            _permissions.AsReadOnly();

        public static Role Create(
            string name,
            string? description = null)
        {
            return new Role(
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

        public void AddPermission(Permission permission)
        {
            ArgumentNullException.ThrowIfNull(permission);

            if (_permissions.Any(x => x.Id == permission.Id))
            {
                return;
            }

            _permissions.Add(permission);
        }

        public void RemovePermission(Guid permissionId)
        {
            var permission = _permissions
                .FirstOrDefault(x => x.Id == permissionId);

            if (permission is null)
            {
                return;
            }

            _permissions.Remove(permission);
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
