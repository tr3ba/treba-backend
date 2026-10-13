using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
    {
        public void Configure(
            EntityTypeBuilder<Wishlist> builder
        )
        {
            builder.ToTable("wishlists");

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.UserId)
                .IsRequired();

            builder
                .Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(255);

            builder
                .Property(x => x.DefaultWishlist)
                .IsRequired();

            builder
                .Property(x => x.CreatedAt)
                .IsRequired();

            builder
                .HasOne<Domain.Entities.Users.User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.UserId);
        }
    }

}
