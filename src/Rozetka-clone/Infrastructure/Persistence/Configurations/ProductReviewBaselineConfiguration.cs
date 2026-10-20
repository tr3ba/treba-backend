using Domain.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Infrastructure.Persistence.Configurations;

public sealed class ProductReviewBaselineConfiguration : IEntityTypeConfiguration<ProductReviewBaseline>
{
    public void Configure(EntityTypeBuilder<ProductReviewBaseline> builder)
    {
        builder.ToTable("product_review_baselines");
        builder.HasKey(x => x.ProductId);
        builder.Property(x => x.Snapshot).HasColumnType("jsonb").IsRequired();
        builder.HasOne<Product>().WithOne().HasForeignKey<ProductReviewBaseline>(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
