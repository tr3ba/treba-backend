using Domain.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderNumber)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(x => x.OrderNumber)
            .IsUnique();

        builder.Property(x => x.CustomerId)
            .IsRequired();

        builder.HasIndex(x => x.CustomerId);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // TODO: Remove obsolete fields after implementing Payment and Delivery
        builder.Property(x => x.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        //
        // builder.Property(x => x.DeliveryStatus)
        //     .HasConversion<string>()
        //     .HasMaxLength(32)
        //     .IsRequired();

        builder.Property(x => x.Currency)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.Subtotal)
            .HasPrecision(18, 2);

        builder.Property(x => x.DiscountAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.DeliveryPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.RecipientName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.RecipientPhone)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.RecipientEmail)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Version)
            .IsConcurrencyToken().ValueGeneratedNever();

    }
}
