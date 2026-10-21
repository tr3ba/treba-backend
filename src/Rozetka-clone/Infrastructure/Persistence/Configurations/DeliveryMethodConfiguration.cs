using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class DeliveryMethodConfiguration : IEntityTypeConfiguration<DeliveryMethod>
    {
        public void Configure(EntityTypeBuilder<DeliveryMethod> builder)
        {
            builder.ToTable("delivery_methods");

            builder.HasKey(x => x.Id);

            builder.HasIndex(x => x.Code)
                .IsUnique();

            builder.Property(x => x.Name)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.Code)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.Description)
                .HasMaxLength(500);

            builder.Property(x => x.BasePrice)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.Active)
                .IsRequired();

            builder.HasMany(x => x.Shipments)
                .WithOne(x => x.DeliveryMethod)
                .HasForeignKey(x => x.DeliveryMethodId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
