using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ProductAttributeValueConfiguration
        : IEntityTypeConfiguration<ProductAttributeValue>
    {
        public void Configure(EntityTypeBuilder<ProductAttributeValue> builder)
        {
            builder.ToTable("product_attribute_values");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.StringValue)
                .HasMaxLength(2000);

            builder.Property(x => x.NumberValue)
                .HasPrecision(18, 4);

            builder.HasOne<Domain.Entities.Product.Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Domain.Entities.Product.ProductVariant>()
                .WithMany()
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne<Domain.Entities.Attribute.Attribute>()
                .WithMany()
                .HasForeignKey(x => x.AttributeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Domain.Entities.Attribute.AttributeOption>()
                .WithMany()
                .HasForeignKey(x => x.OptionId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(x => x.ProductId);
            builder.HasIndex(x => x.VariantId);
            builder.HasIndex(x => x.AttributeId);
            builder.HasIndex(x => x.OptionId);

            builder.HasIndex(x => new
            {
                x.ProductId,
                x.VariantId,
                x.AttributeId,
                x.OptionId
            });
        }
    }
}
