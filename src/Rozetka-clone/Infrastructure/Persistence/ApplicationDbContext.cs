using Application.Abstractions;
using Domain.Entities;
using Domain.Entities.Attribute;
using Domain.Entities.Inventory;
using Domain.Entities.Orders;
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

        public DbSet<User> Users => Set<User>();

        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

        public DbSet<Address> Addresses => Set<Address>();

        public DbSet<Review> Reviews => Set<Review>();

        public DbSet<ReviewImage> ReviewImages => Set<ReviewImage>();

        public DbSet<ReviewVote> ReviewVotes => Set<ReviewVote>();

        public DbSet<SellerReviewResponse> SellerReviewResponses => Set<SellerReviewResponse>();

        public DbSet<Role> Roles => Set<Role>();

        public DbSet<AuthenticationChallenge> AuthenticationChallenges => Set<AuthenticationChallenge>();

        public DbSet<Permission> Permissions => Set<Permission>();

        public DbSet<Product> Products => Set<Product>();

        public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

        public DbSet<ProductImage> ProductImages => Set<ProductImage>();

        public DbSet<ProductTag> ProductTags => Set<ProductTag>();

        public DbSet<Category> Categories => Set<Category>();

        public DbSet<Brand> Brands => Set<Brand>();

        public DbSet<DomainAttribute> Attributes => Set<DomainAttribute>();

        public DbSet<AttributeOption> AttributeOptions => Set<AttributeOption>();

        public DbSet<Seller> Sellers => Set<Seller>();

        public DbSet<Store> Stores => Set<Store>();

        public DbSet<Cart> Carts => Set<Cart>();

        public DbSet<CartItem> CartItems => Set<CartItem>();

        public DbSet<Wishlist> Wishlists => Set<Wishlist>();

        public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

        public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();

        public DbSet<ProductTagRelation> ProductTagRelations => Set<ProductTagRelation>();

        public DbSet<Warehouse> Warehouses => Set<Warehouse>();

        public DbSet<Domain.Entities.Inventory.Inventory> Inventory => Set<Domain.Entities.Inventory.Inventory>();

        public DbSet<StockMovement> StockMovements => Set<StockMovement>();

        public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

        public DbSet<Order> Orders => Set<Order>();

        public DbSet<OrderItem> OrderItems => Set<OrderItem>();

        public DbSet<OrderAddress> OrderAddresses => Set<OrderAddress>();

        public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();

        public DbSet<Domain.Entities.Payments.Payment> Payments => Set<Domain.Entities.Payments.Payment>();

        public DbSet<Domain.Entities.Payments.PaymentTransaction> PaymentTransactions => Set<Domain.Entities.Payments.PaymentTransaction>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        }
    }
}