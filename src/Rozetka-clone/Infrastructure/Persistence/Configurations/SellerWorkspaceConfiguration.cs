using Domain.Entities;
using Domain.Entities.Product;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class SellerWorkspaceConfiguration : IEntityTypeConfiguration<SellerImportJob>, IEntityTypeConfiguration<TelegramNotification>, IEntityTypeConfiguration<ProductIdentity>
{
    public void Configure(EntityTypeBuilder<SellerImportJob> b)
    {
        b.ToTable("seller_import_jobs"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SourceUrl).HasMaxLength(1500).IsRequired(); b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Error).HasMaxLength(2000); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.UserId, x.CreatedAt }); b.HasIndex(x => new { x.Status, x.LeaseUntil });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductImportDraft>().WithMany().HasForeignKey(x => x.PreviewId).OnDelete(DeleteBehavior.SetNull);
    }
    public void Configure(EntityTypeBuilder<TelegramNotification> b)
    {
        b.ToTable("telegram_notifications"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ExpectedRole).HasMaxLength(40).IsRequired(); b.Property(x => x.ExpectedStatus).HasMaxLength(40).IsRequired();
        b.Property(x => x.Text).HasMaxLength(3500).IsRequired(); b.Property(x => x.View).HasMaxLength(20).IsRequired();
        b.Property(x => x.Result).HasMaxLength(100); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompletedAt, x.AvailableAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<ProductIdentity> b)
    {
        b.ToTable("product_identities"); b.HasKey(x => x.ProductId); b.Property(x => x.ProductId).ValueGeneratedNever();
        b.Property(x => x.ManufacturerPartNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedPartNumber).HasMaxLength(100).IsRequired(); b.HasIndex(x => x.NormalizedPartNumber);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
