using Domain.Entities;
using Domain.Entities.Attribute;
using Domain.Entities.Inventory;
using Domain.Entities.Product;
using Domain.Entities.ProductTag;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using DomainAttribute = Domain.Entities.Attribute.Attribute;

namespace Application.Abstractions
{
    public interface IApplicationDbContext
    {
        DbSet<User> Users { get; }

        DbSet<UserProfile> UserProfiles { get; }

        DbSet<Address> Addresses { get; }

        DbSet<Role> Roles { get; }

        DbSet<AuthenticationChallenge> AuthenticationChallenges { get; }

        DbSet<Permission> Permissions { get; }

        DbSet<Product> Products { get; }

        DbSet<ProductVariant> ProductVariants { get; }

        DbSet<ProductImage> ProductImages { get; }

        DbSet<ProductTag> ProductTags { get; }

        DbSet<ProductTagRelation> ProductTagRelations { get; }

        DbSet<Category> Categories { get; }

        DbSet<Brand> Brands { get; }

        DbSet<DomainAttribute> Attributes { get; }

        DbSet<AttributeOption> AttributeOptions { get; }

        DbSet<ProductAttributeValue> ProductAttributeValues { get; }

        DbSet<Seller> Sellers { get; }
        DbSet<Store> Stores { get; }

        DbSet<Cart> Carts { get; }

        DbSet<CartItem> CartItems { get; }

        public DbSet<Warehouse> Warehouses { get; }

        public DbSet<Domain.Entities.Inventory.Inventory> Inventory { get; }

        public DbSet<StockMovement> StockMovements { get; }

        public DbSet<InventoryReservation> InventoryReservations { get; }

        Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default
        );
    }
}
