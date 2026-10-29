using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication;

public sealed class PasswordResetService(
    IApplicationDbContext db, IRegistrationService verificationMethods, IEmailSender email, ISmsSender sms,
    IOptions<SecurityOptions> security, ILogger<PasswordResetService> logger) : IPasswordResetService
{
    public const string RequestMessage = "Якщо акаунт існує та обраний контакт підтверджено, ми надішлемо код. Перевірте пошту або SMS. Якщо код не надходить, спробуйте пізніше.";
    private const string InvalidCode = "Невірний або прострочений код підтвердження. Надішліть код ще раз.";

    public async Task<PasswordResetChallengeResponse> RequestAsync(RequestPasswordResetRequest request, CancellationToken ct = default)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), null, true))
            throw new ArgumentException("Вкажіть електронну пошту акаунта та оберіть спосіб підтвердження: email або sms.");
        if (!(await verificationMethods.GetMethodsAsync(ct)).Methods.Contains(request.VerificationMethod))
            throw new InvalidOperationException("Обраний спосіб підтвердження тимчасово недоступний. Оберіть інший спосіб.");

        var now = DateTimeOffset.UtcNow;
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(security.Value.ChallengeLifetimeMinutes, 2, 15));
        var expiresAt = now.Add(lifetime);
        PasswordResetChallengeResponse Response(Guid id) => new(id, request.VerificationMethod, expiresAt, now.AddSeconds(30), RequestMessage);
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, ct);
        var isSms = request.VerificationMethod == "sms";
        // Never let an unverified profile contact become an account recovery credential.
        // The response shape/message also cover unknown, unavailable and throttled accounts.
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active
            || (isSms ? !user.PhoneVerified || string.IsNullOrWhiteSpace(user.Phone) : !user.EmailVerified))
            return Response(Guid.NewGuid());

        var destination = isSms ? user.Phone! : user.Email;
        var recent = db.AuthenticationChallenges.Where(x => (x.UserId == user.Id || x.DeliveryAddress == destination)
            && (x.Purpose == AuthenticationChallengePurpose.RegistrationEmail || x.Purpose == AuthenticationChallengePurpose.RegistrationSms
                || x.Purpose == AuthenticationChallengePurpose.PasswordResetEmail || x.Purpose == AuthenticationChallengePurpose.PasswordResetSms)
            && x.LastCodeSentAt > now.AddHours(-1));
        if (await recent.AnyAsync(x => x.LastCodeSentAt > now.AddSeconds(-30), ct) || await recent.CountAsync(ct) >= 5)
            return Response(Guid.NewGuid());

        var previous = await db.AuthenticationChallenges.Where(x => x.UserId == user.Id && x.ConsumedAt == null
            && (x.Purpose == AuthenticationChallengePurpose.PasswordResetEmail || x.Purpose == AuthenticationChallengePurpose.PasswordResetSms)).ToListAsync(ct);
        foreach (var old in previous) old.Consume();
        var purpose = isSms ? AuthenticationChallengePurpose.PasswordResetSms : AuthenticationChallengePurpose.PasswordResetEmail;
        var challenge = AuthenticationChallenge.Create(user.Id, purpose, expiresAt);
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        challenge.SetDeliveryAddress(destination);
        // Including the current password hash invalidates codes after any password change.
        challenge.SetCode(Hash(challenge.Id, user.PasswordHash, code), expiresAt, now);
        db.AuthenticationChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        try
        {
            if (isSms)
                await sms.SendAsync(destination, $"TREBA: kod zminy parolia {code}. Diie {lifetime.TotalMinutes:0} khv.", challenge.Id, ct);
            else
                await email.SendSecurityCodeAsync(destination, $"{user.FirstName} {user.LastName}", code, lifetime, ct);
        }
        catch (Exception ex) when (ex is SmsDeliveryException or SmtpException or HttpRequestException or InvalidOperationException)
        {
            // Do not expose account existence, addresses, security codes or provider details.
            logger.LogWarning("Password reset code delivery failed ({FailureType}).", ex.GetType().Name);
        }
        return Response(challenge.Id);
    }

    public async Task CompleteAsync(CompletePasswordResetRequest request, CancellationToken ct = default)
    {
        if (request.NewPassword is null || request.NewPassword.Length is < 8 or > 72
            || Encoding.UTF8.GetByteCount(request.NewPassword) > 72
            || !request.NewPassword.Any(c => c is >= 'a' and <= 'z')
            || !request.NewPassword.Any(c => c is >= 'A' and <= 'Z')
            || !request.NewPassword.Any(c => c is >= '0' and <= '9'))
            throw new ArgumentException("Пароль має містити 8–72 символи, великі й малі латинські літери та цифру (до 72 байтів UTF-8).");
        var challenge = await db.AuthenticationChallenges.SingleOrDefaultAsync(x => x.Id == request.ChallengeId, ct);
        if (challenge is null || challenge.Purpose is not (AuthenticationChallengePurpose.PasswordResetEmail or AuthenticationChallengePurpose.PasswordResetSms)
            || !challenge.IsActive(DateTimeOffset.UtcNow))
            throw new UnauthorizedAccessException(InvalidCode);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == challenge.UserId, ct);
        var isSms = challenge.Purpose == AuthenticationChallengePurpose.PasswordResetSms;
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active
            || (isSms ? !user.PhoneVerified || string.IsNullOrWhiteSpace(user.Phone) : !user.EmailVerified)
            || challenge.DeliveryAddress != (isSms ? user.Phone : user.Email))
            throw new UnauthorizedAccessException(InvalidCode);

        if (request.Code is null || request.Code.Length != 6 || request.Code.Any(c => c is < '0' or > '9')
            || string.IsNullOrWhiteSpace(challenge.CodeHash)
            || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(challenge.CodeHash), Convert.FromBase64String(Hash(challenge.Id, user.PasswordHash, request.Code))))
        {
            challenge.RegisterFailure();
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException(InvalidCode);
        }

        // Commit password, session revocation and all outstanding auth challenges together.
        // Existing 2FA methods stay enabled, and the caller must sign in again normally.
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(request.NewPassword));
        user.RevokeRefreshToken();
        var outstanding = await db.AuthenticationChallenges.Where(x => x.UserId == user.Id && x.ConsumedAt == null).ToListAsync(ct);
        foreach (var item in outstanding) item.Consume();
        await db.SaveChangesAsync(ct);
    }

    private string Hash(Guid challengeId, string passwordHash, string code)
    {
        var key = Convert.FromBase64String(security.Value.OtpHashKey);
        if (key.Length < 32) throw new InvalidOperationException("Security:OtpHashKey must decode to at least 32 bytes.");
        return Convert.ToBase64String(HMACSHA256.HashData(key,
            Encoding.UTF8.GetBytes($"{challengeId:N}:password-reset:{passwordHash}:{code}")));
    }
}
