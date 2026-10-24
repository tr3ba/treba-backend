using Domain.Entities.Orders;
using Domain.Entities.Product;
using Domain.Entities.Support;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.IsDeleted).IsConcurrencyToken();
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.CustomerReadAt).IsConcurrencyToken();
        builder.Property(x => x.StaffReadAt).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.UpdatedAt });
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => new { x.OrderId, x.Kind }).IsUnique().HasFilter("NOT \"IsDeleted\" AND \"Kind\" = 'RETURN' AND \"Status\" IN ('OPEN', 'IN_PROGRESS', 'APPROVED')");
        builder.HasIndex(x => x.CustomerId).IsUnique()
            .HasFilter("NOT \"IsDeleted\" AND \"Kind\" = 'CHAT' AND \"Status\" <> 'CLOSED'");
        builder.HasIndex(x => new { x.CustomerId, x.UpdatedAt });
        builder.HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique()
            .HasFilter("NOT \"IsDeleted\" AND \"Kind\" = 'QUESTION' AND \"ProductId\" IS NOT NULL AND \"Status\" <> 'CLOSED'");
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> builder)
    {
        builder.ToTable("support_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AuthorName).HasMaxLength(320).IsRequired();
        builder.Property(x => x.AuthorRole).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        builder.HasIndex(x => new { x.TicketId, x.CreatedAt });
        builder.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}
