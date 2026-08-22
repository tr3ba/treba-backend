using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class AddressConfiguration
    : IEntityTypeConfiguration<Address>
    {
        public void Configure(EntityTypeBuilder<Address> builder)
        {
            builder.ToTable("addresses");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.UserId)
                .IsRequired();

            builder.Property(x => x.Country)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.Region)
                .HasMaxLength(100);

            builder.Property(x => x.City)
                .HasMaxLength(150)
                .IsRequired();

            builder.Property(x => x.Street)
                .HasMaxLength(250)
                .IsRequired();

            builder.Property(x => x.Building)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.Apartment)
                .HasMaxLength(50);

            builder.Property(x => x.PostalCode)
                .HasMaxLength(20);

            builder.Property(x => x.RecipientName)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.RecipientPhone)
                .HasMaxLength(30)
                .IsRequired();

            builder.Property(x => x.DefaultAddress)
                .IsRequired();
        }
    }
}
