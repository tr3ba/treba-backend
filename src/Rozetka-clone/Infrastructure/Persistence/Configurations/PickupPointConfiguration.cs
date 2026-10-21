using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class PickupPointConfiguration : IEntityTypeConfiguration<PickupPoint>
    {
        public void Configure(EntityTypeBuilder<PickupPoint> builder)
        {
            builder.ToTable("pickup_points");

            builder.HasKey(x => x.Id);

            builder.HasIndex(x => new { x.Provider, x.ExternalId })
                .IsUnique();

            builder.HasIndex(x => x.City);

            builder.Property(x => x.Provider)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.ExternalId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.City)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.Address)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.Latitude)
                .HasPrecision(9, 6);

            builder.Property(x => x.Longitude)
                .HasPrecision(9, 6);

            builder.Property(x => x.Active)
                .IsRequired();
        }
    }
}
