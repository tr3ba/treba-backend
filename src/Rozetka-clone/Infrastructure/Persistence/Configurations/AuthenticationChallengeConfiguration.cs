using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class AuthenticationChallengeConfiguration : IEntityTypeConfiguration<AuthenticationChallenge>
{
    public void Configure(
        EntityTypeBuilder<AuthenticationChallenge> builder
    )
    {
        builder.ToTable("authentication_challenges");
        builder.HasKey(challenge => challenge.Id);
        builder
            .Property(challenge => challenge.Purpose)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder
            .Property(challenge => challenge.CodeHash)
            .HasMaxLength(128);
        builder
            .Property(challenge => challenge.ExpiresAt)
            .IsRequired();
        builder
            .Property(challenge => challenge.CreatedAt)
            .IsRequired();
        builder.HasIndex(
            challenge =>
                new
                {
                    challenge.UserId,
                    challenge.Purpose,
                    challenge.ExpiresAt,
                }
        );
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(challenge => challenge.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
