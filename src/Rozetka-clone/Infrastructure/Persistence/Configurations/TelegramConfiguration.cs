using Domain.Entities;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class TelegramConfiguration : IEntityTypeConfiguration<TelegramBinding>, IEntityTypeConfiguration<TelegramLinkChallenge>,
    IEntityTypeConfiguration<TelegramGuardChallenge>, IEntityTypeConfiguration<SellerCategoryGrant>,
    IEntityTypeConfiguration<RozetkaCategoryMapping>, IEntityTypeConfiguration<ProductImportDraft>, IEntityTypeConfiguration<TelegramBotCursor>
{
    public void Configure(EntityTypeBuilder<TelegramBotCursor> b)
    { b.ToTable("telegram_bot_cursors"); b.HasKey(x => x.BotId); b.Property(x => x.BotId).ValueGeneratedNever(); }
    public void Configure(EntityTypeBuilder<TelegramBinding> b)
    {
        b.ToTable("telegram_bindings"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.UserId).IsUnique(); b.HasIndex(x => x.TelegramUserId).IsUnique();
        b.Property(x => x.Username).HasMaxLength(100);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<TelegramLinkChallenge> b)
    {
        b.ToTable("telegram_link_challenges"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.ExpiresAt); b.HasIndex(x => x.UserId); b.Property(x => x.Username).HasMaxLength(100);
        b.Property(x => x.TelegramName).HasMaxLength(200); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<TelegramGuardChallenge> b)
    {
        b.ToTable("telegram_guard_challenges"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<AuthenticationChallenge>().WithMany().HasForeignKey(x => x.LoginChallengeId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.BrowserSecretHash).HasMaxLength(64).IsRequired(); b.Property(x => x.ApprovalSecretHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.DeviceName).HasMaxLength(200); b.Property(x => x.ClientIp).HasMaxLength(64);
        b.HasIndex(x => x.ExpiresAt); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedUserId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<SellerCategoryGrant> b)
    {
        b.ToTable("seller_category_grants"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.SellerId, x.CategoryId }).IsUnique();
        b.HasOne<Seller>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<RozetkaCategoryMapping> b)
    {
        b.ToTable("rozetka_category_mappings"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ExternalKey).HasMaxLength(20).IsRequired(); b.HasIndex(x => x.ExternalKey).IsUnique();
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<ProductImportDraft> b)
    {
        b.ToTable("product_import_drafts"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SourceProductId).HasMaxLength(30); b.Property(x => x.SourceUrl).HasMaxLength(1500);
        b.Property(x => x.SourceCategoryKey).HasMaxLength(20); b.Property(x => x.SourceCategoryName).HasMaxLength(250);
        b.Property(x => x.Name).HasMaxLength(250); b.Property(x => x.Description).HasMaxLength(10000); b.Property(x => x.BrandName).HasMaxLength(200);
        b.Property(x => x.ImagesJson).HasColumnType("jsonb"); b.Property(x => x.CharacteristicsJson).HasColumnType("jsonb");
        b.Property(x => x.SourcePrice).HasPrecision(12,2); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.UserId, x.SourceProductId }).IsUnique().HasFilter("\"SubmittedAt\" IS NOT NULL"); b.HasIndex(x => x.ExpiresAt);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
