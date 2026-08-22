using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions
{
    public interface IApplicationDbContext
    {
        DbSet<User> Users { get; }

        DbSet<UserProfile> UserProfiles { get; }

        DbSet<Address> Addresses { get; }

        DbSet<Role> Roles { get; }

        DbSet<Permission> Permissions { get; }

        Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
