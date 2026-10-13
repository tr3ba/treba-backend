using Domain.Entities;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ReviewVoteConfiguration : IEntityTypeConfiguration<ReviewVote>
    {
        public void Configure(EntityTypeBuilder<ReviewVote> builder)
        {
            builder.ToTable("review_votes");

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.ReviewId)
                .IsRequired();

            builder
                .Property(x => x.UserId)
                .IsRequired();

            builder
                .Property(x => x.Helpful)
                .IsRequired();

            builder
                .HasOne<Review>()
                .WithMany()
                .HasForeignKey(x => x.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.ReviewId);

            builder.HasIndex(x => x.UserId);

            builder
                .HasIndex(x => new
                {
                    x.ReviewId,
                    x.UserId
                })
                .IsUnique();
        }
    }

}
