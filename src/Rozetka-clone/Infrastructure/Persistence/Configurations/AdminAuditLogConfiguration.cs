using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> builder)
    {
        builder.ToTable("admin_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorName).HasMaxLength(320);
        builder.Property(x => x.ActorRole).HasMaxLength(50);
        builder.Property(x => x.Action).HasMaxLength(20);
        builder.Property(x => x.EntityType).HasMaxLength(64);
        builder.Property(x => x.EntityName).HasMaxLength(500);
        builder.Property(x => x.ChangesJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.OccurredAt).IsDescending();
        builder.HasIndex(x => x.ActorId);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
