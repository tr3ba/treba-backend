using Domain.Entities;
using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
    {
        public void Configure(
            EntityTypeBuilder<WishlistItem> builder
        )
        {
            builder.ToTable("wishlist_items");

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.WishlistId)
                .IsRequired();

            builder
                .Property(x => x.ProductId)
                .IsRequired();

            builder
                .Property(x => x.AddedAt)
                .IsRequired();

            builder
                .HasOne<Wishlist>()
                .WithMany()
                .HasForeignKey(x => x.WishlistId)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.WishlistId);

            builder.HasIndex(x => x.ProductId);

            builder
                .HasIndex(
                    x =>
                        new
                        {
                            x.WishlistId,
                            x.ProductId
                        }
                )
                .IsUnique();
        }
    }

}
