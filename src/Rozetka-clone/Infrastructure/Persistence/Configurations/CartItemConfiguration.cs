using System;
using System.Collections.Generic;
using System.Text;
using Domain.Entities;
using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
    {
        public void Configure(
            EntityTypeBuilder<CartItem> builder
        )
        {
            builder.ToTable("cart_items");

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.CartId)
                .IsRequired();

            builder
                .Property(x => x.ProductVariantId)
                .IsRequired();

            builder
                .Property(x => x.Quantity)
                .IsRequired();

            builder
                .Property(x => x.CreatedAt)
                .IsRequired();

            builder
                .Property(x => x.UpdatedAt)
                .IsRequired();

            builder
                .HasOne<Cart>()
                .WithMany()
                .HasForeignKey(x => x.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne<ProductVariant>()
                .WithMany()
                .HasForeignKey(x => x.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.CartId);

            builder.HasIndex(x => x.ProductVariantId);

            builder
                .HasIndex(
                    x =>
                        new
                        {
                            x.CartId,
                            x.ProductVariantId
                        }
                )
                .IsUnique();
        }
    }
}
