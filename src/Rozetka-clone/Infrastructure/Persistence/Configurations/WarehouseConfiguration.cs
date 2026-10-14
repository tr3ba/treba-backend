using Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(
        EntityTypeBuilder<Warehouse> builder
    )
    {
        builder.ToTable("warehouses");

        builder.HasKey(x => x.Id);

        builder
            .Property(x => x.SellerId)
            .IsRequired();

        builder
            .Property(x => x.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder
            .Property(x => x.Country)
            .HasMaxLength(100)
            .IsRequired();

        builder
            .Property(x => x.City)
            .HasMaxLength(100)
            .IsRequired();

        builder
            .Property(x => x.Address)
            .HasMaxLength(250)
            .IsRequired();

        builder
            .Property(x => x.Active)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(x => x.SellerId);
    }
}
