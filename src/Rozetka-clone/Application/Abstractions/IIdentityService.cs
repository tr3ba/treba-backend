using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Abstractions
{
    public interface IIdentityService
    {
        Task<Guid> CreateUserAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default);

        Task<bool> CheckPasswordAsync(
            Guid userId,
            string password,
            CancellationToken cancellationToken = default);

        Task ChangePasswordAsync(
            Guid userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default);

        Task<bool> IsInRoleAsync(
            Guid userId,
            string role,
            CancellationToken cancellationToken = default);

        Task AddToRoleAsync(
            Guid userId,
            string role,
            CancellationToken cancellationToken = default);
    }
}
