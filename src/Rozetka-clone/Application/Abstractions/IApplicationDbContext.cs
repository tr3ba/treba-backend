using Domain.Entities.Product;
using Domain.Entities.ProductTag;
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

        DbSet<Product> Products { get; }

        DbSet<ProductVariant> ProductVariants { get; }

        DbSet<ProductImage> ProductImages { get; }

        DbSet<ProductTag> ProductTags { get; }

        DbSet<ProductTagRelation> ProductTagRelations { get; }

        Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default);

    }
}
