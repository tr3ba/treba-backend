using Domain.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class OrderAddressConfiguration : IEntityTypeConfiguration<OrderAddress>
{
    public void Configure(EntityTypeBuilder<OrderAddress> builder)
    {
        builder.ToTable("order_addresses");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Country).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Region).HasMaxLength(100);
        builder.Property(x => x.City).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Street).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Building).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Apartment).HasMaxLength(50);
        builder.Property(x => x.PostalCode).HasMaxLength(20);
        builder.Property(x => x.RecipientName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.RecipientPhone).HasMaxLength(32).IsRequired();
    }
}