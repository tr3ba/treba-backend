using Domain.Entities;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class SellerApplicationConfiguration : IEntityTypeConfiguration<SellerApplication>
{
    public void Configure(EntityTypeBuilder<SellerApplication> b)
    {
        b.ToTable("seller_applications"); b.HasKey(x => x.Id);
        b.HasIndex(x => x.UserId).IsUnique(); b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CompanyName).HasMaxLength(250); b.Property(x => x.StoreName).HasMaxLength(200);
        b.Property(x => x.TaxNumber).HasMaxLength(100); b.Property(x => x.Website).HasMaxLength(1000);
        b.Property(x => x.ContactName).HasMaxLength(200); b.Property(x => x.Position).HasMaxLength(150);
        b.Property(x => x.Email).HasMaxLength(320); b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Status).HasMaxLength(20); b.Property(x => x.ReviewNote).HasMaxLength(2000);
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}

public sealed class CustomerPreferencesConfiguration : IEntityTypeConfiguration<CustomerPreferences>
{
    public void Configure(EntityTypeBuilder<CustomerPreferences> b)
    {
        b.ToTable("customer_preferences"); b.HasKey(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.ValuesJson).HasMaxLength(16000); b.Property(x => x.Version).IsConcurrencyToken();
    }
}

public sealed class NotificationReceiptConfiguration : IEntityTypeConfiguration<NotificationReceipt>
{
    public void Configure(EntityTypeBuilder<NotificationReceipt> b)
    {
        b.ToTable("notification_receipts"); b.HasKey(x => new { x.UserId, x.EventKey });
        b.Property(x => x.EventKey).HasMaxLength(120);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
