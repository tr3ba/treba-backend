using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.Configurations
{
    public sealed class CouponUsageConfiguration
    : IEntityTypeConfiguration<CouponUsage>
    {
        public void Configure(EntityTypeBuilder<CouponUsage> builder)
        {
            builder.ToTable("coupon_usages");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.CouponId)
                .IsRequired();

            builder.Property(x => x.UserId)
                .IsRequired();

            builder.Property(x => x.OrderId)
                .IsRequired();

            builder.Property(x => x.UsedAt)
                .IsRequired();

            builder.HasOne<Coupon>()
                .WithMany()
                .HasForeignKey(x => x.CouponId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.CouponId);
            builder.HasIndex(x => x.UserId);
            builder.HasIndex(x => x.OrderId);
        }
    }
}
