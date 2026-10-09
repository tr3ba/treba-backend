using Application.Abstractions;
using Domain.Entities;
using Domain.Entities.Attribute;
using Domain.Entities.Inventory;
using Domain.Entities.Product;
using Domain.Entities.ProductTag;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using DomainAttribute = Domain.Entities.Attribute.Attribute;

namespace Infrastructure.Persistence
{
    public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options
        )
            : base(options)
        {

        }

        public DbSet<User> Users
        {
            get
            {
                return Set<User>();
            }
        }

        public DbSet<UserProfile> UserProfiles
        {
            get
            {
                return Set<UserProfile>();
            }
        }

        public DbSet<Address> Addresses
        {
            get
            {
                return Set<Address>();
            }
        }

        public DbSet<Role> Roles
        {
            get
            {
                return Set<Role>();
            }
        }

        public DbSet<AuthenticationChallenge> AuthenticationChallenges
        {
            get
            {
                return Set<AuthenticationChallenge>();
            }
        }

        public DbSet<Permission> Permissions
        {
            get
            {
                return Set<Permission>();
            }
        }

        public DbSet<Product> Products
        {
            get
            {
                return Set<Product>();
            }
        }

        public DbSet<ProductVariant> ProductVariants
        {
            get
            {
                return Set<ProductVariant>();
            }
        }

        public DbSet<ProductImage> ProductImages
        {
            get
            {
                return Set<ProductImage>();
            }
        }

        public DbSet<ProductTag> ProductTags
        {
            get
            {
                return Set<ProductTag>();
            }
        }

        public DbSet<Category> Categories
        {
            get
            {
                return Set<Category>();
            }
        }

        public DbSet<Brand> Brands
        {
            get
            {
                return Set<Brand>();
            }
        }

        public DbSet<DomainAttribute> Attributes
        {
            get
            {
                return Set<DomainAttribute>();
            }
        }

        public DbSet<AttributeOption> AttributeOptions
        {
            get
            {
                return Set<AttributeOption>();
            }
        }

        public DbSet<Seller> Sellers
        {
            get
            {
                return Set<Seller>();
            }
        }
        public DbSet<Store> Stores
        {
            get
            {
                return Set<Store>();
            }
        }

        public DbSet<Cart> Carts
        {
            get
            {
                return Set<Cart>();
            }
        }

        public DbSet<CartItem> CartItems
        {
            get
            {
                return Set<CartItem>();
            }
        }

        public DbSet<ProductAttributeValue> ProductAttributeValues
        {
            get
            {
                return Set<ProductAttributeValue>();
            }
        }

        public DbSet<ProductTagRelation> ProductTagRelations
        {
            get
            {
                return Set<ProductTagRelation>();
            }
        }

        public DbSet<Warehouse> Warehouses
        {
            get
            {
                return Set<Warehouse>();
            }
        }

        public DbSet<Domain.Entities.Inventory.Inventory> Inventory
        {
            get
            {
                return Set<Domain.Entities.Inventory.Inventory>();
            }
        }

        public DbSet<StockMovement> StockMovements
        {
            get
            {
                return Set<StockMovement>();
            }
        }

        public DbSet<InventoryReservation> InventoryReservations
        {
            get
            {
                return Set<InventoryReservation>();
            }
        }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        }
    }
}
