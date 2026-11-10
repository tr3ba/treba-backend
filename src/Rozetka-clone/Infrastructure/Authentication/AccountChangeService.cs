using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication;

public sealed class AccountChangeService(IApplicationDbContext db, ISecondFactorService factors,
    IEmailSender email, IOptions<SecurityOptions> security)
{
    public async Task<AccountChangeChallengeResponse> BeginAsync(Guid userId, BeginAccountChangeRequest request,
        bool changingEmail, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (!user.EmailVerified || user.Status != UserStatus.Active)
            throw new UnauthorizedAccessException("Verify the current email before changing account credentials.");
        var target = request.Target.Trim().ToLowerInvariant();
        if (changingEmail)
        {
            if (!MailAddress.TryCreate(target, out var address) || address.Address != target || target == user.Email)
                throw new ArgumentException("Enter a different valid email address.");
            if (await db.Users.AnyAsync(u => u.Email == target, ct))
                throw new InvalidOperationException("This email cannot be used.");
        }
        else if (target is not ("google" or "facebook"))
            throw new ArgumentException("Unknown provider.");

        if (user.AuthenticatorEnabled || user.EmailTwoFactorEnabled
            || await db.TelegramBindings.AnyAsync(b => b.UserId == user.Id, ct))
        {
            if (request.SecondFactor is null)
                throw new UnauthorizedAccessException("Confirm a fresh login with your current second factor.");
            var verified = await factors.VerifyLoginChallengeAsync(request.SecondFactor, ct);
            if (verified.Id != userId)
                throw new UnauthorizedAccessException("The confirmation belongs to another account.");
        }
        await using var operation = await SecurityOperationLock.AcquireAsync(db, "account-change:" + userId, ct);
        if (db is DbContext currentContext)
            await currentContext.Entry(user).ReloadAsync(ct);
        if (!user.EmailVerified || user.IsDeleted || user.Status != UserStatus.Active)
            throw new UnauthorizedAccessException("Account confirmation is unavailable.");
        var now = DateTimeOffset.UtcNow;
        if (await db.AuthenticationChallenges.AnyAsync(c => c.UserId == userId
            && (c.Purpose == AuthenticationChallengePurpose.EmailChange || c.Purpose == AuthenticationChallengePurpose.ExternalProviderLink)
            && c.CreatedAt > now.AddSeconds(-30), ct))
            throw new InvalidOperationException("Wait 30 seconds before requesting another confirmation.");
        if (await db.AuthenticationChallenges.CountAsync(c => c.UserId == userId
            && (c.Purpose == AuthenticationChallengePurpose.EmailChange || c.Purpose == AuthenticationChallengePurpose.ExternalProviderLink)
            && c.CreatedAt > now.AddHours(-1), ct) >= 5)
            throw new InvalidOperationException("The hourly confirmation limit has been reached.");
        var purpose = changingEmail ? AuthenticationChallengePurpose.EmailChange : AuthenticationChallengePurpose.ExternalProviderLink;
        foreach (var previous in await db.AuthenticationChallenges.Where(c => c.UserId == userId && c.Purpose == purpose && c.ConsumedAt == null).ToListAsync(ct))
            previous.Consume();
        var challenge = AuthenticationChallenge.Create(userId, purpose, now.AddMinutes(10));
        var currentCode = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
        var newCode = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
        challenge.SetDeliveryAddress(target);
        challenge.SetAccountChange(user.Email, changingEmail ? Hash(challenge.Id, newCode) : null);
        challenge.SetCode(Hash(challenge.Id, currentCode), challenge.ExpiresAt, now);
        db.AuthenticationChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await operation.CommitAsync(ct);
        await email.SendSecurityCodeAsync(user.Email, user.FirstName, currentCode, TimeSpan.FromMinutes(10), ct);
        if (changingEmail)
            await email.SendSecurityCodeAsync(target, user.FirstName, newCode, TimeSpan.FromMinutes(10), ct);
        return new(challenge.Id, challenge.ExpiresAt);
    }

    public async Task<string> ConfirmAsync(Guid userId, Guid id, string code, string? newCode,
        bool changingEmail, string? expectedTarget, CancellationToken ct)
    {
        await using var operation = await SecurityOperationLock.AcquireAsync(db, "account-change:" + userId, ct);
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (db is DbContext context)
            await context.Entry(user).ReloadAsync(ct);
        if (!user.EmailVerified || user.IsDeleted || user.Status != UserStatus.Active)
            throw new UnauthorizedAccessException("Account confirmation is unavailable.");
        var purpose = changingEmail ? AuthenticationChallengePurpose.EmailChange : AuthenticationChallengePurpose.ExternalProviderLink;
        var challenge = await db.AuthenticationChallenges.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId && c.Purpose == purpose, ct);
        if (challenge is null || !challenge.IsActive(DateTimeOffset.UtcNow) || challenge.AccountEmailSnapshot != user.Email
            || (expectedTarget is not null && challenge.DeliveryAddress != expectedTarget))
            throw new UnauthorizedAccessException("The account confirmation is invalid or expired.");
        if (!Matches(challenge.CodeHash, Hash(id, code))
            || changingEmail && !Matches(challenge.SecondaryCodeHash, Hash(id, newCode ?? "")))
        {
            challenge.RegisterFailure();
            await db.SaveChangesAsync(ct);
            await operation.CommitAsync(ct);
            throw new UnauthorizedAccessException("Invalid confirmation code.");
        }
        var target = challenge.DeliveryAddress!;
        if (changingEmail)
        {
            if (await db.Users.AnyAsync(u => u.Id != userId && u.Email == target, ct))
                throw new InvalidOperationException("This email cannot be used.");
            var emailFactor = user.EmailTwoFactorEnabled;
            user.ChangeEmail(target);
            user.VerifyEmail();
            if (emailFactor) user.EnableEmailTwoFactor();
            foreach (var pending in await db.AuthenticationChallenges.Where(c => c.UserId == userId && c.ConsumedAt == null).ToListAsync(ct))
                pending.Consume();
        }
        challenge.Consume();
        await db.SaveChangesAsync(ct);
        await operation.CommitAsync(ct);
        return target;
    }

    private string Hash(Guid id, string code) => Convert.ToBase64String(HMACSHA256.HashData(
        Convert.FromBase64String(security.Value.OtpHashKey), Encoding.UTF8.GetBytes($"{id:N}:{code}")));
    private static bool Matches(string? expected, string actual) => expected is not null
        && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(expected), Convert.FromBase64String(actual));
}
