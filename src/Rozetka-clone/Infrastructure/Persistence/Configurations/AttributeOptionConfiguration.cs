using Domain.Entities.Attribute;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class AttributeOptionConfiguration
        : IEntityTypeConfiguration<AttributeOption>
    {
        public void Configure(EntityTypeBuilder<AttributeOption> builder)
        {
            builder.ToTable("attribute_options");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Value)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.DisplayValue)
                .HasMaxLength(200);

            builder.Property(x => x.SortOrder)
                .IsRequired();

            builder.HasOne<Domain.Entities.Attribute.Attribute>()
                .WithMany()
                .HasForeignKey(x => x.AttributeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.AttributeId);

            builder.HasIndex(x => new
            {
                x.AttributeId,
                x.Value
            })
            .IsUnique();
        }
    }

}
