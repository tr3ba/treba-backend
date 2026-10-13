using Domain.Entities;
using Domain.Entities.Product;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
    {
        public void Configure(EntityTypeBuilder<Review> builder)
        {
            builder.ToTable(
                "reviews",
                tableBuilder =>
                {
                    tableBuilder.HasCheckConstraint(
                        "CK_reviews_rating",
                        "\"Rating\" >= 1 AND \"Rating\" <= 5"
                    );
                }
            );

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.ProductId)
                .IsRequired();

            builder
                .Property(x => x.UserId)
                .IsRequired();

            builder
                .Property(x => x.OrderItemId)
                .IsRequired();

            builder
                .Property(x => x.Rating)
                .IsRequired();

            builder
                .Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(255);

            builder
                .Property(x => x.Comment)
                .IsRequired();

            builder
                .Property(x => x.Status)
                .IsRequired();

            builder
                .Property(x => x.VerifiedPurchase)
                .IsRequired();

            builder
                .Property(x => x.HelpfulCount)
                .IsRequired();

            builder
                .Property(x => x.CreatedAt)
                .IsRequired();

            builder
                .Property(x => x.UpdatedAt)
                .IsRequired();

            builder
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.ProductId);

            builder.HasIndex(x => x.UserId);

            builder
                .HasIndex(x => x.OrderItemId)
                .IsUnique();
        }
    }

}
