using System;
using System.Collections.Generic;
using System.Text;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class SellerConfiguration : IEntityTypeConfiguration<Seller>
    {
        public void Configure(
            EntityTypeBuilder<Seller> builder
        )
        {
            builder.ToTable("sellers");

            builder.HasKey(x => x.Id);

            builder
                .Property(x => x.Id)
                .ValueGeneratedNever();

            builder
                .Property(x => x.UserId)
                .IsRequired();

            builder
                .HasIndex(x => x.UserId)
                .IsUnique();

            builder
                .Property(x => x.CompanyName)
                .HasMaxLength(250)
                .IsRequired();

            builder
                .Property(x => x.TaxNumber)
                .HasMaxLength(100)
                .IsRequired();

            builder
                .HasIndex(x => x.TaxNumber)
                .IsUnique();

            builder
                .Property(x => x.Description)
                .HasMaxLength(2000);

            builder
                .Property(x => x.Phone)
                .HasMaxLength(50);

            builder
                .Property(x => x.Email)
                .HasMaxLength(320);

            builder
                .Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            builder
                .Property(x => x.CreatedAt)
                .IsRequired();

            builder
                .Property(x => x.UpdatedAt)
                .IsRequired();

            builder
                .HasOne<Domain.Entities.Users.User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
