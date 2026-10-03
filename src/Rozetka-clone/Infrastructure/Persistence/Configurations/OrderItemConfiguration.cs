using Domain.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.VariantId).IsRequired();
        builder.Property(x => x.SellerId).IsRequired();

        builder.Property(x => x.ProductName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.VariantName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Sku).HasMaxLength(64).IsRequired();

        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.TotalPrice).HasPrecision(18, 2);

        builder.Property(x => x.ProductSnapshot).HasColumnType("jsonb"); 

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.SellerId);
    }
}