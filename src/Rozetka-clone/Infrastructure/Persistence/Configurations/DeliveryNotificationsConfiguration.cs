using Domain.Entities;
using Domain.Entities.Orders;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class DeliveryQuoteConfiguration : IEntityTypeConfiguration<DeliveryQuote>
{
    public void Configure(EntityTypeBuilder<DeliveryQuote> b)
    {
        b.ToTable("delivery_quotes"); b.HasKey(x => x.Id);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.Fingerprint).HasMaxLength(64);
        b.Property(x => x.City).HasMaxLength(200); b.Property(x => x.PickupPoint).HasMaxLength(500);
        b.Property(x => x.Price).HasPrecision(18, 2); b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.DeclaredValue).HasPrecision(18, 2); b.Property(x => x.WeightKg).HasPrecision(12, 3);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.ExpiresAt);
    }
}
public sealed class OrderShipmentConfiguration : IEntityTypeConfiguration<OrderShipment>
{
    public void Configure(EntityTypeBuilder<OrderShipment> b)
    {
        b.ToTable("order_shipments"); b.HasKey(x => x.OrderId);
        b.HasOne<Order>().WithOne().HasForeignKey<OrderShipment>(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Carrier).HasMaxLength(20);
        b.Property(x => x.City).HasMaxLength(200); b.Property(x => x.PickupPoint).HasMaxLength(500);
        b.Property(x => x.Reference).HasMaxLength(64); b.HasIndex(x => x.Reference).IsUnique();
        b.Property(x => x.QuotedPrice).HasPrecision(18, 2); b.Property(x => x.WeightKg).HasPrecision(12, 3);
    }
}
public sealed class CustomerNotificationSettingsConfiguration : IEntityTypeConfiguration<CustomerNotificationSettings>
{
    public void Configure(EntityTypeBuilder<CustomerNotificationSettings> b)
    {
        b.ToTable("customer_notification_settings"); b.HasKey(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}
public sealed class CustomerNotificationConfiguration : IEntityTypeConfiguration<CustomerNotification>
{
    public void Configure(EntityTypeBuilder<CustomerNotification> b)
    {
        b.ToTable("customer_notifications"); b.HasKey(x => x.Id);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.EventKey, x.UserId, x.Channel }).IsUnique();
        b.HasIndex(x => new { x.CompletedAt, x.AvailableAt });
        b.Property(x => x.EventKey).HasMaxLength(120); b.Property(x => x.Channel).HasMaxLength(20);
        b.Property(x => x.Kind).HasMaxLength(20); b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Text).HasMaxLength(2000); b.Property(x => x.Path).HasMaxLength(300);
        b.Property(x => x.Result).HasMaxLength(100); b.Property(x => x.Version).IsConcurrencyToken();
    }
}
