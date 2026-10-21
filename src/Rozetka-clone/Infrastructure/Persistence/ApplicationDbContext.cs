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
    public sealed partial class ApplicationDbContext : DbContext, IApplicationDbContext
    {
        private readonly ICurrentActor? _actor;
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentActor? actor = null
        )
            : base(options)
        {
            _actor = actor;
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<ProductReviewBaseline> ProductReviewBaselines => Set<ProductReviewBaseline>();
        public DbSet<Domain.Entities.Support.SupportTicket> SupportTickets => Set<Domain.Entities.Support.SupportTicket>();
        public DbSet<Domain.Entities.Support.SupportMessage> SupportMessages => Set<Domain.Entities.Support.SupportMessage>();
        public DbSet<Domain.Entities.Telegram.SellerImportJob> SellerImportJobs => Set<Domain.Entities.Telegram.SellerImportJob>();
        public DbSet<Domain.Entities.Telegram.TelegramNotification> TelegramNotifications => Set<Domain.Entities.Telegram.TelegramNotification>();
        public DbSet<Domain.Entities.Telegram.ProductIdentity> ProductIdentities => Set<Domain.Entities.Telegram.ProductIdentity>();
        public DbSet<Domain.Entities.Telegram.TelegramBotCursor> TelegramBotCursors => Set<Domain.Entities.Telegram.TelegramBotCursor>();
        public DbSet<Domain.Entities.Telegram.TelegramBinding> TelegramBindings => Set<Domain.Entities.Telegram.TelegramBinding>();
        public DbSet<Domain.Entities.Telegram.TelegramLinkChallenge> TelegramLinkChallenges => Set<Domain.Entities.Telegram.TelegramLinkChallenge>();
        public DbSet<Domain.Entities.Telegram.TelegramGuardChallenge> TelegramGuardChallenges => Set<Domain.Entities.Telegram.TelegramGuardChallenge>();
        public DbSet<Domain.Entities.Telegram.SellerCategoryGrant> SellerCategoryGrants => Set<Domain.Entities.Telegram.SellerCategoryGrant>();
        public DbSet<Domain.Entities.Telegram.RozetkaCategoryMapping> RozetkaCategoryMappings => Set<Domain.Entities.Telegram.RozetkaCategoryMapping>();
        public DbSet<Domain.Entities.Telegram.ProductImportDraft> ProductImportDrafts => Set<Domain.Entities.Telegram.ProductImportDraft>();

        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

        public DbSet<Address> Addresses => Set<Address>();

        public DbSet<Review> Reviews => Set<Review>();

        public DbSet<ReviewImage> ReviewImages => Set<ReviewImage>();

        public DbSet<ReviewVote> ReviewVotes => Set<ReviewVote>();

        public DbSet<Promotion> Promotions => Set<Promotion>();

        public DbSet<PromotionRule> PromotionRules => Set<PromotionRule>();

        public DbSet<PromotionAction> PromotionActions => Set<PromotionAction>();

        public DbSet<Coupon> Coupons => Set<Coupon>();

        public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();

        public DbSet<SellerReviewResponse> SellerReviewResponses => Set<SellerReviewResponse>();

        public DbSet<Role> Roles => Set<Role>();

        public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();

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
            modelBuilder.Entity<Product>().Property(p => p.UpdatedAt).IsConcurrencyToken();
            modelBuilder.Entity<ProductVariant>().Property(v => v.Price).IsConcurrencyToken();
            modelBuilder.Entity<ProductVariant>().Property(v => v.OldPrice).IsConcurrencyToken();
            modelBuilder.Entity<ProductVariant>().Property(v => v.StockQuantity).IsConcurrencyToken();
            modelBuilder.Entity<ProductVariant>().Property(v => v.IsActive).IsConcurrencyToken();
            modelBuilder.Entity<Order>().Property(o => o.Status).IsConcurrencyToken();
            modelBuilder.Entity<Promotion>().Property(p => p.Version).IsConcurrencyToken();
            modelBuilder.Entity<Coupon>().Property(c => c.UsedCount).IsConcurrencyToken();
            modelBuilder.Entity<Coupon>().Property(c => c.Active).IsConcurrencyToken();
            modelBuilder.Entity<Coupon>().Property(c => c.UsageLimit).IsConcurrencyToken();
            modelBuilder.Entity<Coupon>().Property(c => c.UsageLimitPerUser).IsConcurrencyToken();
            modelBuilder.Entity<InventoryReservation>().Property(r => r.Status).IsConcurrencyToken();
        }
    }
}
