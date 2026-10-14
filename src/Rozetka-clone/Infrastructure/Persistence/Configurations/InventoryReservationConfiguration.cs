using Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(
        EntityTypeBuilder<InventoryReservation> builder
    )
    {
        builder.ToTable("inventory_reservations");

        builder.HasKey(x => x.Id);

        builder
            .Property(x => x.OrderId)
            .IsRequired();

        builder
            .Property(x => x.VariantId)
            .IsRequired();

        builder
            .Property(x => x.WarehouseId)
            .IsRequired();

        builder
            .Property(x => x.Quantity)
            .IsRequired();

        // Храним enum в виде строкового значения (ACTIVE, CONFIRMED, etc.)
        builder
            .Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder
            .Property(x => x.ExpiresAt)
            .IsRequired();

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.Status);
    }
}
