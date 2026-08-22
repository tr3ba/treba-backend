using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class UserProfileConfiguration
    : IEntityTypeConfiguration<UserProfile>
    {
        public void Configure(EntityTypeBuilder<UserProfile> builder)
        {
            builder.ToTable("user_profiles");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.UserId)
                .IsRequired();

            builder.HasIndex(x => x.UserId)
                .IsUnique();

            builder.Property(x => x.Gender)
                .HasMaxLength(30);

            builder.Property(x => x.AvatarUrl)
                .HasMaxLength(1000);

            builder.Property(x => x.Language)
                .HasMaxLength(10)
                .IsRequired();

            builder.Property(x => x.MarketingEmailsEnabled)
                .IsRequired();
        }
    }
}
