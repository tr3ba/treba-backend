using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class AttributeConfiguration
        : IEntityTypeConfiguration<Domain.Entities.Attribute.Attribute>
    {
        public void Configure(
            EntityTypeBuilder<Domain.Entities.Attribute.Attribute> builder)
        {
            builder.ToTable("attributes");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.Code)
                .HasMaxLength(100)
                .IsRequired();

            builder.HasIndex(x => x.Code)
                .IsUnique();

            builder.Property(x => x.Type)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.Unit)
                .HasMaxLength(50);

            builder.Property(x => x.IsRequired)
                .IsRequired();

            builder.Property(x => x.IsFilterable)
                .IsRequired();

            builder.Property(x => x.IsComparable)
                .IsRequired();

            builder.Property(x => x.SortOrder)
                .IsRequired();

            builder.HasOne<Domain.Entities.Category>()
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.CategoryId);
        }
    }
}
