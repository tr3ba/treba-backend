using Domain.Entities.Product;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ProductImageChangeConfiguration : IEntityTypeConfiguration<ProductImageChange>
    {
        public void Configure(EntityTypeBuilder<ProductImageChange> builder)
        {
            builder.ToTable("product_image_changes");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.ProductId)
                .IsRequired();

            builder.Property(x => x.ChangeType)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.Property<uint>("xmin")
                .IsRowVersion();

            builder.Property(x => x.ImageUrl)
                .HasMaxLength(2048);

            builder.Property(x => x.AltText)
                .HasMaxLength(500);

            builder.Property(x => x.CreatedAt)
                .IsRequired();

            builder.Property(x => x.RejectionReason)
                .HasMaxLength(1000);

            builder.HasIndex(x => new { x.ProductId, x.Status });

            builder.HasIndex(x => new { x.ImageId, x.Status });

            builder.HasIndex(x => x.ImageId)
                .IsUnique()
                .HasFilter("\"ImageId\" IS NOT NULL AND \"Status\" = 'Pending'");

            builder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<ProductImage>()
                .WithMany()
                .HasForeignKey(x => x.ImageId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne<ProductVariant>()
                .WithMany()
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }

}
