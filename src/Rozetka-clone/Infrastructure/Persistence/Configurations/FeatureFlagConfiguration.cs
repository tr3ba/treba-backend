using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => x.Key).IsUnique();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
