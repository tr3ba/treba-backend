using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.ToTable("products");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.StoreId)
                .IsRequired();

            builder.Property(x => x.CategoryId)
                .IsRequired();

            builder.Property(x => x.BrandId)
                .IsRequired();

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(250);

            builder.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(300);

            builder.HasIndex(x => x.Slug)
                .IsUnique();

            builder.Property(x => x.ShortDescription)
                .HasMaxLength(500);

            builder.Property(x => x.Description);

            builder.Property(x => x.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);

            builder.Property(x => x.AverageRating)
                .HasDefaultValue(0);

            builder.Property(x => x.ReviewCount)
                .HasDefaultValue(0);

            builder.Property(x => x.SalesCount)
                .HasDefaultValue(0);

            builder.Property(x => x.WarrantyMonth)
                .HasDefaultValue(0);

            builder.Property(x => x.CountryOfOrigin)
                .HasMaxLength(100);

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.UpdatedAt)
                .IsRequired();

            builder.HasIndex(x => x.CategoryId);
            builder.HasIndex(x => x.BrandId);
            builder.HasIndex(x => x.StoreId);
            builder.HasIndex(x => x.Status);
        }
    }
}
