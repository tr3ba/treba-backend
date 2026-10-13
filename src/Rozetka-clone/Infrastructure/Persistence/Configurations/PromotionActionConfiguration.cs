using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class PromotionActionConfiguration
    : IEntityTypeConfiguration<PromotionAction>
    {
        public void Configure(EntityTypeBuilder<PromotionAction> builder)
        {
            builder.ToTable("promotion_actions");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.PromotionId)
                .IsRequired();

            builder.Property(x => x.DiscountType)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(x => x.DiscountValue)
                .IsRequired()
                .HasPrecision(18, 2);

            builder.Property(x => x.MaximumDiscount)
                .HasPrecision(18, 2);

            builder.HasOne<Promotion>()
                .WithMany()
                .HasForeignKey(x => x.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.PromotionId);
        }

    }
}
