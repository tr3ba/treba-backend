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
    IApplicationDbContext db,
    IRegistrationService verificationMethods,
    IEmailSender email,
    ISmsSender sms,
    IOptions<SecurityOptions> security,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    public const string RequestMessage = "Якщо акаунт існує та обраний контакт підтверджено, ми надішлемо код. Перевірте пошту або SMS. Якщо код не надходить, спробуйте пізніше.";

    private const string InvalidCode = "Невірний або прострочений код підтвердження. Надішліть код ще раз.";

    public async Task<PasswordResetChallengeResponse> RequestAsync(
        RequestPasswordResetRequest request,
        CancellationToken ct = default)
    {
        var requestIsValid = Validator.TryValidateObject(request, new ValidationContext(request), null, true);

        if (!requestIsValid)
        {
            throw new ArgumentException(
                "Вкажіть електронну пошту акаунта та оберіть спосіб підтвердження: email або sms.");
        }

        var availableMethods = await verificationMethods.GetMethodsAsync(ct);

        if (!availableMethods.Methods.Contains(request.VerificationMethod))
        {
            throw new InvalidOperationException(
                "Обраний спосіб підтвердження тимчасово недоступний. Оберіть інший спосіб.");
        }

        var now = DateTimeOffset.UtcNow;
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(security.Value.ChallengeLifetimeMinutes, 2, 15));
        var expiresAt = now.Add(lifetime);

        PasswordResetChallengeResponse CreateResponse(Guid id) => new(
            id,
            request.VerificationMethod,
            expiresAt,
            now.AddSeconds(30),
            RequestMessage);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        await using var reservation = await SecurityOperationLock.AcquireAsync(db, "password-reset-codes", ct);
        var user = await db.Users.SingleOrDefaultAsync(item => item.Email == normalizedEmail, ct);
        var isSms = request.VerificationMethod == "sms";

        // Never let an unverified profile contact become an account recovery credential.
        // The response shape/message also cover unknown, unavailable and throttled accounts.
        if (user is null)
            return CreateResponse(Guid.NewGuid());

        var contactIsVerified = isSms
            ? user.PhoneVerified && !string.IsNullOrWhiteSpace(user.Phone)
            : user.EmailVerified;
        var userCannotResetPassword = user.IsDeleted
            || user.Status != UserStatus.Active
            || !contactIsVerified;

        if (userCannotResetPassword)
            return CreateResponse(Guid.NewGuid());

        var destination = isSms ? user.Phone! : user.Email;
        var recentChallenges = db.AuthenticationChallenges.Where(challenge =>
            (challenge.UserId == user.Id || challenge.DeliveryAddress == destination)
            && (challenge.Purpose == AuthenticationChallengePurpose.PasswordResetEmail
                || challenge.Purpose == AuthenticationChallengePurpose.PasswordResetSms)
            && challenge.LastCodeSentAt > now.AddHours(-1));

        var wasSentRecently = await recentChallenges
            .AnyAsync(challenge => challenge.LastCodeSentAt > now.AddSeconds(-30), ct);

        if (wasSentRecently)
            return CreateResponse(Guid.NewGuid());

        var hourlyLimitReached = await recentChallenges.CountAsync(ct) >= 5;

        if (hourlyLimitReached)
            return CreateResponse(Guid.NewGuid());

        var previousChallenges = await db.AuthenticationChallenges
            .Where(challenge =>
                challenge.UserId == user.Id
                && challenge.ConsumedAt == null
                && (challenge.Purpose == AuthenticationChallengePurpose.PasswordResetEmail
                    || challenge.Purpose == AuthenticationChallengePurpose.PasswordResetSms))
            .ToListAsync(ct);

        foreach (var previousChallenge in previousChallenges)
            previousChallenge.Consume();

        var purpose = isSms
            ? AuthenticationChallengePurpose.PasswordResetSms
            : AuthenticationChallengePurpose.PasswordResetEmail;
        var challenge = AuthenticationChallenge.Create(user.Id, purpose, expiresAt);
        var code = RandomNumberGenerator.GetInt32(1_000_000)
            .ToString("D6", CultureInfo.InvariantCulture);
        challenge.SetDeliveryAddress(destination);

        // Including the current password hash invalidates codes after any password change.
        challenge.SetCode(Hash(challenge.Id, user.PasswordHash, code), expiresAt, now);
        db.AuthenticationChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await reservation.CommitAsync(ct);

        try
        {
            if (isSms)
            {
                await sms.SendAsync(
                    destination,
                    $"TREBA: kod zminy parolia {code}. Diie {lifetime.TotalMinutes:0} khv.",
                    challenge.Id,
                    ct);
            }
            else
            {
                await email.SendSecurityCodeAsync(
                    destination,
                    $"{user.FirstName} {user.LastName}",
                    code,
                    lifetime,
                    ct);
            }
        }
        catch (Exception ex) when (ex is SmsDeliveryException or SmtpException or HttpRequestException or InvalidOperationException)
        {
            // Do not expose account existence, addresses, security codes or provider details.
            logger.LogWarning("Password reset code delivery failed ({FailureType}).", ex.GetType().Name);
        }

        return CreateResponse(challenge.Id);
    }

    public async Task CompleteAsync(CompletePasswordResetRequest request, CancellationToken ct = default)
    {
        if (request.NewPassword is null)
            throw new ArgumentException("Пароль має містити 8–72 символи, великі й малі латинські літери та цифру (до 72 байтів UTF-8).");

        var passwordHasValidLength = request.NewPassword.Length is >= 8 and <= 72
            && Encoding.UTF8.GetByteCount(request.NewPassword) <= 72;
        var passwordHasLowercaseLetter = request.NewPassword.Any(character => character is >= 'a' and <= 'z');
        var passwordHasUppercaseLetter = request.NewPassword.Any(character => character is >= 'A' and <= 'Z');
        var passwordHasDigit = request.NewPassword.Any(character => character is >= '0' and <= '9');

        if (!passwordHasValidLength
            || !passwordHasLowercaseLetter
            || !passwordHasUppercaseLetter
            || !passwordHasDigit)
        {
            throw new ArgumentException(
                "Пароль має містити 8–72 символи, великі й малі латинські літери та цифру (до 72 байтів UTF-8).");
        }

        var challenge = await db.AuthenticationChallenges
            .SingleOrDefaultAsync(item => item.Id == request.ChallengeId, ct);

        if (challenge is null)
            throw new UnauthorizedAccessException(InvalidCode);

        var isPasswordResetChallenge = challenge.Purpose is
            AuthenticationChallengePurpose.PasswordResetEmail or AuthenticationChallengePurpose.PasswordResetSms;

        if (!isPasswordResetChallenge || !challenge.IsActive(DateTimeOffset.UtcNow))
            throw new UnauthorizedAccessException(InvalidCode);

        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == challenge.UserId, ct);

        if (user is null)
            throw new UnauthorizedAccessException(InvalidCode);

        var isSms = challenge.Purpose == AuthenticationChallengePurpose.PasswordResetSms;
        var contactIsVerified = isSms
            ? user.PhoneVerified && !string.IsNullOrWhiteSpace(user.Phone)
            : user.EmailVerified;
        var contactMatchesChallenge = challenge.DeliveryAddress == (isSms ? user.Phone : user.Email);
        var userCannotCompleteReset = user.IsDeleted
            || user.Status != UserStatus.Active
            || !contactIsVerified
            || !contactMatchesChallenge;

        if (userCannotCompleteReset)
            throw new UnauthorizedAccessException(InvalidCode);

        var codeHasExpectedFormat = request.Code is { Length: 6 }
            && request.Code.All(character => character is >= '0' and <= '9');
        var challengeHasCodeHash = !string.IsNullOrWhiteSpace(challenge.CodeHash);
        var codeMatchesHash = codeHasExpectedFormat
            && challengeHasCodeHash
            && CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(challenge.CodeHash!),
                Convert.FromBase64String(Hash(challenge.Id, user.PasswordHash, request.Code!)));

        if (!codeMatchesHash)
        {
            challenge.RegisterFailure();
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException(InvalidCode);
        }

        // Commit password, session revocation and all outstanding auth challenges together.
        // Existing 2FA methods stay enabled, and the caller must sign in again normally.
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(request.NewPassword));
        user.RevokeRefreshToken();

        var outstandingChallenges = await db.AuthenticationChallenges
            .Where(item => item.UserId == user.Id && item.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var outstandingChallenge in outstandingChallenges)
            outstandingChallenge.Consume();

        await db.SaveChangesAsync(ct);
    }

    private string Hash(Guid challengeId, string passwordHash, string code)
    {
        var key = Convert.FromBase64String(security.Value.OtpHashKey);

        if (key.Length < 32)
            throw new InvalidOperationException("Security:OtpHashKey must decode to at least 32 bytes.");

        var message = Encoding.UTF8.GetBytes($"{challengeId:N}:password-reset:{passwordHash}:{code}");
        var hash = HMACSHA256.HashData(key, message);
        return Convert.ToBase64String(hash);
    }
}
