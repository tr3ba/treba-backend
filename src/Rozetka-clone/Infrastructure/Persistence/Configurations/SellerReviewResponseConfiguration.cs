using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class SellerReviewResponseConfiguration
        : IEntityTypeConfiguration<SellerReviewResponse>
    {
        public void Configure(EntityTypeBuilder<SellerReviewResponse> builder)
        {
            builder.ToTable("seller_review_responses");

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.ReviewId)
                .IsRequired();

            builder
                .Property(x => x.SellerId)
                .IsRequired();

            builder
                .Property(x => x.Comment)
                .IsRequired();

            builder
                .Property(x => x.CreatedAt)
                .IsRequired();

            builder
                .Property(x => x.UpdatedAt)
                .IsRequired();

            builder
                .HasOne<Review>()
                .WithMany()
                .HasForeignKey(x => x.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne<Seller>()
                .WithMany()
                .HasForeignKey(x => x.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasIndex(x => x.ReviewId)
                .IsUnique();

            builder.HasIndex(x => x.SellerId);
        }
    }

}
