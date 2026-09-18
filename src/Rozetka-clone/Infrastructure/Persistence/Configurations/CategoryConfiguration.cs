using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.ToTable("categories");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.Slug)
                .HasMaxLength(250)
                .IsRequired();

            builder.HasIndex(x => x.Slug)
                .IsUnique();

            builder.Property(x => x.Description)
                .HasMaxLength(2000);

            builder.Property(x => x.ImageUrl)
                .HasMaxLength(1000);

            builder.Property(x => x.IsActive)
                .IsRequired();

            builder.Property(x => x.SortOrder)
                .IsRequired();

            builder.Property(x => x.Level)
                .IsRequired();

            builder.HasOne<Category>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => x.ParentId);

            builder.HasIndex(x => new
            {
                x.ParentId,
                x.SortOrder
            });
        }
    }
}
