using Domain.Entities.Product;
using Domain.Entities.ProductTag;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class ProductTagRelationConfiguration
    : IEntityTypeConfiguration<ProductTagRelation>
    {
        public void Configure(EntityTypeBuilder<ProductTagRelation> builder)
        {
            builder.ToTable("product_tag_relations");

            builder.HasKey(x => new
            {
                x.ProductId,
                x.TagId
            });

            builder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<ProductTag>()
                .WithMany()
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.TagId);
        }
    }
}
