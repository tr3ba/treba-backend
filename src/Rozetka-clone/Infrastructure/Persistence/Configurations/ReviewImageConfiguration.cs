using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ReviewImageConfiguration : IEntityTypeConfiguration<ReviewImage>
    {
        public void Configure(EntityTypeBuilder<ReviewImage> builder)
        {
            builder.ToTable("review_images");

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.ReviewId)
                .IsRequired();

            builder
                .Property(x => x.ImageUrl)
                .IsRequired()
                .HasMaxLength(2048);

            builder
                .Property(x => x.SortOrder)
                .IsRequired();

            builder
                .HasOne<Review>()
                .WithMany()
                .HasForeignKey(x => x.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.ReviewId);
        }
    }

}
