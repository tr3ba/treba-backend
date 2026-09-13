using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ProductVariantConfiguration
    : IEntityTypeConfiguration<ProductVariant>
    {
        public void Configure(EntityTypeBuilder<ProductVariant> builder)
        {
            builder.ToTable("product_variants");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.ProductId)
                .IsRequired();

            builder.Property(x => x.Sku)
                .IsRequired()
                .HasMaxLength(100);

            builder.HasIndex(x => x.Sku)
                .IsUnique();

            builder.Property(x => x.Barcode)
                .HasMaxLength(100);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(250);

            builder.Property(x => x.Price)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.OldPrice)
                .HasPrecision(18, 2);

            builder.Property(x => x.CostPrice)
                .HasPrecision(18, 2);

            builder.Property(x => x.Weight);
            builder.Property(x => x.Length);
            builder.Property(x => x.Width);
            builder.Property(x => x.Height);

            builder.Property(x => x.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.HasIndex(x => x.ProductId);

            builder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
