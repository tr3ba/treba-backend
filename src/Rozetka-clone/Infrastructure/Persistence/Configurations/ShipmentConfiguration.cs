using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
    {
        public void Configure(EntityTypeBuilder<Shipment> builder)
        {
            builder.ToTable("shipments");

            builder.HasKey(x => x.Id);

            builder.HasIndex(x => x.OrderId);

            builder.HasIndex(x => x.DeliveryMethodId);

            builder.HasIndex(x => x.TrackingNumber);

            builder.Property(x => x.Provider)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.TrackingNumber)
                .HasMaxLength(100);

            builder.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            builder.Property(x => x.Price)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.EstimatedDeliveryDate);

            builder.Property(x => x.ShippedAt);

            builder.Property(x => x.DeliveredAt);

            builder.HasOne<Domain.Entities.Orders.Order>()
                .WithMany(x => x.Shipments)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.DeliveryMethod)
                .WithMany(x => x.Shipments)
                .HasForeignKey(x => x.DeliveryMethodId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.Items)
                .WithOne(x => x.Shipment)
                .HasForeignKey(x => x.ShipmentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
