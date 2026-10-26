using Domain.Entities;
using Domain.Entities.Attribute;
using Domain.Entities.Inventory;
using Domain.Entities.Orders;
using Domain.Entities.Product;
using Domain.Entities.ProductTag;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using DomainAttribute = Domain.Entities.Attribute.Attribute;

namespace Application.Abstractions
{
    public interface IApplicationDbContext
    {
        DbSet<DeliveryQuote> DeliveryQuotes { get; }
        DbSet<OrderShipment> OrderShipments { get; }
        DbSet<CustomerNotificationSettings> CustomerNotificationSettings { get; }
        DbSet<CustomerNotification> CustomerNotifications { get; }
        DbSet<FeatureFlag> FeatureFlags { get; }
        DbSet<SellerApplication> SellerApplications { get; }
        DbSet<CustomerPreferences> CustomerPreferences { get; }
        DbSet<NotificationReceipt> NotificationReceipts { get; }
        DbSet<Domain.Entities.Support.SupportTicket> SupportTickets
        {
            get;
        }
        DbSet<Domain.Entities.Support.SupportMessage> SupportMessages
        {
            get;
        }
        DbSet<ProductReviewBaseline> ProductReviewBaselines
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.SellerImportJob> SellerImportJobs
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.TelegramNotification> TelegramNotifications
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.ProductIdentity> ProductIdentities
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.TelegramBotCursor> TelegramBotCursors
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.TelegramBinding> TelegramBindings
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.TelegramLinkChallenge> TelegramLinkChallenges
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.TelegramGuardChallenge> TelegramGuardChallenges
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.SellerCategoryGrant> SellerCategoryGrants
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.RozetkaCategoryMapping> RozetkaCategoryMappings
        {
            get;
        }
        DbSet<Domain.Entities.Telegram.ProductImportDraft> ProductImportDrafts
        {
            get;
        }
        DbSet<User> Users
        {
            get;
        }

        DbSet<UserProfile> UserProfiles
        {
            get;
        }

        DbSet<Address> Addresses
        {
            get;
        }

        DbSet<Role> Roles
        {
            get;
        }

        DbSet<AdminAuditLog> AdminAuditLogs
        {
            get;
        }

        DbSet<AuthenticationChallenge> AuthenticationChallenges
        {
            get;
        }

        DbSet<Permission> Permissions
        {
            get;
        }

        DbSet<Product> Products
        {
            get;
        }

        DbSet<ProductVariant> ProductVariants
        {
            get;
        }

        DbSet<ProductImage> ProductImages
        {
            get;
        }

        DbSet<ProductTag> ProductTags
        {
            get;
        }

        DbSet<ProductTagRelation> ProductTagRelations
        {
            get;
        }

        DbSet<Category> Categories
        {
            get;
        }

        DbSet<Brand> Brands
        {
            get;
        }

        DbSet<DomainAttribute> Attributes
        {
            get;
        }

        DbSet<AttributeOption> AttributeOptions
        {
            get;
        }

        DbSet<ProductAttributeValue> ProductAttributeValues
        {
            get;
        }

        DbSet<Wishlist> Wishlists
        {
            get;
        }

        DbSet<Review> Reviews
        {
            get;
        }

        DbSet<ReviewImage> ReviewImages
        {
            get;
        }

        DbSet<ReviewVote> ReviewVotes
        {
            get;
        }

        DbSet<SellerReviewResponse> SellerReviewResponses
        {
            get;
        }

        DbSet<Promotion> Promotions
        {
            get;
        }

        DbSet<PromotionRule> PromotionRules
        {
            get;
        }

        DbSet<PromotionAction> PromotionActions
        {
            get;
        }

        DbSet<Coupon> Coupons
        {
            get;
        }

        DbSet<CouponUsage> CouponUsages
        {
            get;
        }

        DbSet<WishlistItem> WishlistItems
        {
            get;
        }

        DbSet<Seller> Sellers
        {
            get;
        }

        DbSet<Store> Stores
        {
            get;
        }

        DbSet<Cart> Carts
        {
            get;
        }

        DbSet<CartItem> CartItems
        {
            get;
        }

        public DbSet<Warehouse> Warehouses
        {
            get;
        }

        public DbSet<Domain.Entities.Inventory.Inventory> Inventory
        {
            get;
        }

        public DbSet<StockMovement> StockMovements
        {
            get;
        }

        public DbSet<InventoryReservation> InventoryReservations
        {
            get;
        }

        DbSet<Order> Orders
        {
            get;
        }

        DbSet<OrderItem> OrderItems
        {
            get;
        }

        DbSet<OrderAddress> OrderAddresses
        {
            get;
        }

        DbSet<OrderStatusHistory> OrderStatusHistories
        {
            get;
        }

        DbSet<Domain.Entities.Payments.Payment> Payments { get; }

        DbSet<Domain.Entities.Payments.PaymentTransaction> PaymentTransactions { get; }

        DbSet<DeliveryMethod> DeliveryMethods { get; }

        DbSet<Shipment> Shipments { get; }

        DbSet<ShipmentItem> ShipmentItems { get; }

        DbSet<PickupPoint> PickupPoints { get; }

        Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default
        );
    }
}
