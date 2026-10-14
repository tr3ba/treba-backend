using Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class InventoryConfiguration : IEntityTypeConfiguration<Domain.Entities.Inventory.Inventory>
{
    public void Configure(
        EntityTypeBuilder<Domain.Entities.Inventory.Inventory> builder
    )
    {
        builder.ToTable("inventory");

        builder.HasKey(x => x.Id);

        builder
            .Property(x => x.WarehouseId)
            .IsRequired();

        builder
            .Property(x => x.VariantId)
            .IsRequired();

        builder
            .Property(x => x.AvailableQuantity)
            .IsRequired()
            .HasDefaultValue(0);

        builder
            .Property(x => x.ReservedQuantity)
            .IsRequired()
            .HasDefaultValue(0);

        builder
            .Property(x => x.MinimumQuantity)
            .IsRequired()
            .HasDefaultValue(0);

        builder
            .Property(x => x.Version)
            .IsRowVersion();

        builder.Ignore(x => x.AvailableForSale);

        builder
            .HasIndex(
                x =>
                    new
                    {
                        x.WarehouseId,
                        x.VariantId
                    }
            )
            .IsUnique();

        builder
            .HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
