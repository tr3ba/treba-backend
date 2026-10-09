using Domain.Entities.Common;

namespace Domain.Entities.Users;

public enum AuthenticationChallengePurpose
{
    Login,
    EnableEmailTwoFactor,
    DisableEmailTwoFactor,
}

public sealed class AuthenticationChallenge : BaseEntity
{
    private AuthenticationChallenge()
    {

    }

    private AuthenticationChallenge(
        Guid userId,
        AuthenticationChallengePurpose purpose,
        DateTimeOffset expiresAt
    )
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Purpose = purpose;
        ExpiresAt = expiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public AuthenticationChallengePurpose Purpose { get; private set; }
    public string? CodeHash { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastCodeSentAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public int FailedAttempts { get; private set; }

    public bool IsActive(
        DateTimeOffset now
    )
    {
        return ConsumedAt is null
            && ExpiresAt > now
            && FailedAttempts < 5;
    }

    public bool CanSendCode(
        DateTimeOffset now
    )
    {
        return IsActive(now)
            && (
                LastCodeSentAt is null
                || LastCodeSentAt <= now.AddSeconds(-30)
            );
    }

    public static AuthenticationChallenge Create(
        Guid userId,
        AuthenticationChallengePurpose purpose,
        DateTimeOffset expiresAt
    )
    {
        return new(
            userId,
            purpose,
            expiresAt
        );
    }

    public void SetCode(
        string codeHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
        LastCodeSentAt = now;
        FailedAttempts = 0;
    }

    public void RegisterFailure()
    {
        FailedAttempts++;
        if (FailedAttempts >= 5)
        {
            ConsumedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Consume()
    {
        ConsumedAt = DateTimeOffset.UtcNow;
    }
}
