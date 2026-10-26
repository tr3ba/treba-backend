using Domain.Entities.Common;

namespace Domain.Entities.Users;

public sealed class UserExternalLogin : BaseEntity
{
    private UserExternalLogin()
    {
    }

    private UserExternalLogin(
        Guid id,
        Guid userId,
        string provider,
        string providerUserId,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        Provider = provider;
        ProviderUserId = providerUserId;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }

    public User? User { get; private set; }

    public string Provider { get; private set; } = null!;

    public string ProviderUserId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static UserExternalLogin Create(
        Guid userId,
        string provider,
        string providerUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUserId);

        return new UserExternalLogin(
            Guid.NewGuid(),
            userId,
            provider.ToLowerInvariant(),
            providerUserId,
            DateTimeOffset.UtcNow
        );
    }
}