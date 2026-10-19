using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class PromotionRuleConfiguration
    : IEntityTypeConfiguration<PromotionRule>
    {
        public void Configure(EntityTypeBuilder<PromotionRule> builder)
        {
            builder.ToTable("promotion_rules");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.PromotionId)
                .IsRequired();

            builder.Property(x => x.RuleType)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);

            builder.Property(x => x.RuleValue)
                .IsRequired()
                .HasMaxLength(500);

            builder.HasOne<Promotion>()
                .WithMany()
                .HasForeignKey(x => x.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.PromotionId);

            builder.HasIndex(x => new
            {
                x.PromotionId,
                x.RuleType
            });
        }
    }
}
