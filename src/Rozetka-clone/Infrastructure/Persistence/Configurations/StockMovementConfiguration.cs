using Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(
        EntityTypeBuilder<StockMovement> builder
    )
    {
        builder.ToTable("stock_movements");

        builder.HasKey(x => x.Id);

        builder
            .Property(x => x.InventoryId)
            .IsRequired();

        builder
            .Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder
            .Property(x => x.Quantity)
            .IsRequired();

        builder
            .Property(x => x.Reason)
            .HasMaxLength(255)
            .IsRequired();

        builder
            .Property(x => x.ReferenceType)
            .HasMaxLength(50);

        builder
            .Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.InventoryId);

        builder
            .HasOne<Domain.Entities.Inventory.Inventory>()
            .WithMany()
            .HasForeignKey(x => x.InventoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
