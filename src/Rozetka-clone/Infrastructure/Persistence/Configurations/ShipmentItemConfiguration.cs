using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ShipmentItemConfiguration : IEntityTypeConfiguration<ShipmentItem>
    {
        public void Configure(EntityTypeBuilder<ShipmentItem> builder)
        {
            builder.ToTable("shipment_items");

            builder.HasKey(x => x.Id);

            builder.HasIndex(x => x.ShipmentId);

            builder.HasIndex(x => x.OrderItemId);

            builder.HasIndex(x => new { x.ShipmentId, x.OrderItemId })
                .IsUnique();

            builder.Property(x => x.Quantity)
                .IsRequired();

            builder.HasOne(x => x.Shipment)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.ShipmentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.OrderItem)
                .WithMany()
                .HasForeignKey(x => x.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
