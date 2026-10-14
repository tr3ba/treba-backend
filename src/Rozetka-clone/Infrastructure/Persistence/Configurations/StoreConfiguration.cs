using System;
using System.Collections.Generic;
using System.Text;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
    {
        public void Configure(
            EntityTypeBuilder<Store> builder
        )
        {
            builder.ToTable("stores");

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.SellerId)
                .IsRequired();

            builder
                .Property(x => x.Name)
                .HasMaxLength(250)
                .IsRequired();

            builder
                .Property(x => x.Slug)
                .HasMaxLength(250)
                .IsRequired();

            builder
                .HasIndex(x => x.Slug)
                .IsUnique();

            builder
                .Property(x => x.Description)
                .HasMaxLength(2000);

            builder
                .Property(x => x.LogoUrl)
                .HasMaxLength(1000);

            builder
                .Property(x => x.IsActive)
                .IsRequired();

            builder
                .Property(x => x.CreatedAt)
                .IsRequired();

            builder
                .Property(x => x.UpdatedAt)
                .IsRequired();

            builder
                .HasOne<Seller>()
                .WithMany()
                .HasForeignKey(x => x.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.SellerId);
        }
    }
}
