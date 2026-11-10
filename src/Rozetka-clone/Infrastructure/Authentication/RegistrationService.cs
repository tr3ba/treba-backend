using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Authentication;

public sealed class RegistrationService(
    IApplicationDbContext db,
    ITokenService tokens,
    IEmailSender email,
    ISmsSender sms,
    IOptions<SecurityOptions> security,
    IOptions<SmtpEmailOptions> smtp, ILogger<RegistrationService>? logger = null) : IRegistrationService
{
    private const string InvalidConfirmation = "Invalid or expired registration confirmation.";

    private static bool IsRegistration(AuthenticationChallengePurpose purpose) =>
        purpose is AuthenticationChallengePurpose.RegistrationEmail or AuthenticationChallengePurpose.RegistrationSms;

    public async Task<RegistrationMethodsResponse> GetMethodsAsync(CancellationToken ct = default)
    {
        var methods = new List<string>();

        var emailIsConfigured = smtp.Value.Enabled
            && !string.IsNullOrWhiteSpace(smtp.Value.Host)
            && !string.IsNullOrWhiteSpace(smtp.Value.FromAddress);

        if (emailIsConfigured)
            methods.Add("email");

        var smsIsEnabled = sms.IsConfigured
            && await db.FeatureFlags.AsNoTracking()
                .AnyAsync(flag => flag.Key == FeatureFlag.SmsSending && flag.Enabled, ct);

        if (smsIsEnabled)
            methods.Add("sms");

        return new(methods);
    }

    public async Task<RegistrationChallengeResponse> BeginAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var requestIsValid = Validator.TryValidateObject(request, new ValidationContext(request), null, true);

        if (!requestIsValid)
        {
            throw new ArgumentException(
                "Перевірте дані та обов'язково оберіть спосіб підтвердження: email або sms.");
        }

        var requestedNonCustomerRole = !string.IsNullOrWhiteSpace(request.RoleName)
            && request.RoleName != Roles.Customer;

        if (requestedNonCustomerRole)
            throw new InvalidOperationException("Public registration only supports the Customer role.");

        var method = request.VerificationMethod!;
        await RequireMethodAsync(method, ct);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : TurboSmsSender.NormalizePhone(request.PhoneNumber);

        if (method == "sms" && phone is null)
            throw new ArgumentException("Для SMS потрібен номер телефону.");

        var destination = method == "sms" ? phone! : normalizedEmail;
        var purpose = method == "sms"
            ? AuthenticationChallengePurpose.RegistrationSms
            : AuthenticationChallengePurpose.RegistrationEmail;

        var candidateHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        await using var reservation = await SecurityOperationLock.AcquireAsync(db, "registration-codes", ct);
        var user = await db.Users
            .Include(existingUser => existingUser.Role)
            .SingleOrDefaultAsync(existingUser => existingUser.Email == normalizedEmail, ct);

        if (user is not null && db is DbContext tracked)
            await tracked.Entry(user).ReloadAsync(ct);

        var now = DateTimeOffset.UtcNow;
        RegistrationChallengeResponse HiddenResponse() => new(Guid.NewGuid(), method, Mask(destination),
            now.AddMinutes(Math.Clamp(security.Value.ChallengeLifetimeMinutes, 2, 15)), now.AddSeconds(30));
        if (user is not null && (!CanResumePendingRegistration(user)
            || method == "sms" && (user.Phone != phone || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))))
            return HiddenResponse();
        try { await RequireSendLimitAsync(user?.Id, destination, ct); }
        catch (InvalidOperationException) { return HiddenResponse(); }
        if (user is null)
        {
            var role = await db.Roles.SingleOrDefaultAsync(item => item.Name == Roles.Customer, ct)
                ?? throw new InvalidOperationException("Customer role is missing.");

            user = User.Create(Guid.NewGuid(), normalizedEmail, phone, request.FirstName, request.LastName);
            user.SetPasswordHash(candidateHash);
            user.AssignRole(role.Id);
            db.Users.Add(user);
        }
        else
        {
            user.UpdateProfile(request.FirstName, request.LastName, null, phone);
        }

        return await SendAsync(user, purpose, destination, candidateHash, reservation, ct);
    }

    public async Task<RegistrationChallengeResponse> ResendAsync(Guid challengeId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var result = await ResendCoreAsync(challengeId, ct);
            return result with { Method = "", MaskedDestination = "" };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return new(Guid.NewGuid(), "", "", now.AddMinutes(Math.Clamp(security.Value.ChallengeLifetimeMinutes, 2, 15)), now.AddSeconds(30));
        }
    }

    private async Task<RegistrationChallengeResponse> ResendCoreAsync(Guid challengeId, CancellationToken ct)
    {
        await using var reservation = await SecurityOperationLock.AcquireAsync(db, "registration-codes", ct);
        var (challenge, user) = await GetChallengeAsync(challengeId, requireActive: false, ct);
        var method = challenge.Purpose == AuthenticationChallengePurpose.RegistrationSms ? "sms" : "email";

        await RequireMethodAsync(method, ct);
        await RequireSendLimitAsync(user.Id, challenge.DeliveryAddress!, ct);

        return await SendAsync(user, challenge.Purpose, challenge.DeliveryAddress!, challenge.RegistrationPasswordHash ?? throw new UnauthorizedAccessException(InvalidConfirmation), reservation, ct);
    }

    public async Task<AuthResponse> VerifyAsync(VerifyRegistrationRequest request, CancellationToken ct = default)
    {
        await using var reservation = await SecurityOperationLock.AcquireAsync(db, "registration-codes", ct);
        var (challenge, user) = await GetChallengeAsync(request.ChallengeId, requireActive: true, ct);

        var codeHasExpectedFormat = request.Code is { Length: 6 }
            && request.Code.All(character => character is >= '0' and <= '9');
        var challengeHasCodeHash = !string.IsNullOrEmpty(challenge.CodeHash);
        var codeMatchesHash = codeHasExpectedFormat
            && challengeHasCodeHash
            && CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(challenge.CodeHash!),
                Convert.FromBase64String(Hash(challenge.Id, request.Code!)));

        if (!codeMatchesHash)
        {
            challenge.RegisterFailure();
            await db.SaveChangesAsync(ct);
            await reservation.CommitAsync(ct);
            throw new UnauthorizedAccessException(InvalidConfirmation);
        }

        if (string.IsNullOrWhiteSpace(challenge.RegistrationPasswordHash))
            throw new UnauthorizedAccessException(InvalidConfirmation);
        user.SetPasswordHash(challenge.RegistrationPasswordHash);
        if (challenge.Purpose == AuthenticationChallengePurpose.RegistrationSms)
            user.VerifyPhone();
        else
            user.VerifyEmail();

        challenge.Consume();
        user.RegisterLogin();

        var refreshToken = tokens.GenerateRefreshToken();
        user.SetRefreshToken(refreshToken, DateTimeOffset.UtcNow.AddDays(7));

        // Activation, challenge consumption and refresh token commit together; concurrent replay fails.
        await db.SaveChangesAsync(ct);

        await reservation.CommitAsync(ct);
        var role = user.Role?.Name ?? Roles.Customer;
        var accessToken = tokens.GenerateAccessToken(user, role);

        return new(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            role,
            accessToken,
            refreshToken,
            tokens.GetAccessTokenExpiration());
    }

    private static bool CanResumePendingRegistration(User user) =>
        user.Status == UserStatus.PendingActivation && !user.IsDeleted && user.Role?.Name == Roles.Customer;

    private async Task<RegistrationChallengeResponse> SendAsync(
        User user,
        AuthenticationChallengePurpose purpose,
        string destination,
        string passwordHash,
        SecurityOperationLock reservation,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(security.Value.ChallengeLifetimeMinutes, 2, 15));
        var expiresAt = now.Add(lifetime);

        var previousChallenges = await db.AuthenticationChallenges
            .Where(challenge =>
                challenge.UserId == user.Id
                && challenge.ConsumedAt == null
                && (challenge.Purpose == AuthenticationChallengePurpose.RegistrationEmail
                    || challenge.Purpose == AuthenticationChallengePurpose.RegistrationSms))
            .ToListAsync(ct);

        foreach (var previousChallenge in previousChallenges)
            previousChallenge.Consume();

        var challenge = AuthenticationChallenge.Create(user.Id, purpose, expiresAt);
        var code = RandomNumberGenerator.GetInt32(1_000_000)
            .ToString("D6", CultureInfo.InvariantCulture);
        challenge.SetDeliveryAddress(destination);
        challenge.SetRegistrationPassword(passwordHash);
        challenge.SetCode(Hash(challenge.Id, code), expiresAt, now);
        db.AuthenticationChallenges.Add(challenge);

        // Reserve the send and invalidate old codes before calling a paid service. Failures count toward limits.
        await db.SaveChangesAsync(ct);
        await reservation.CommitAsync(ct);

        try
        {
            if (purpose == AuthenticationChallengePurpose.RegistrationSms)
            {
                await sms.SendAsync(
                    destination,
                    $"TREBA: kod reiestratsii {code}. Diie {lifetime.TotalMinutes:0} khv.",
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
        catch (Exception ex) when (ex is SmsDeliveryException or System.Net.Mail.SmtpException
            or HttpRequestException or InvalidOperationException)
        {
            // Delivery failures consume quota and use the same public response as other registrations.
            logger?.LogWarning("Registration delivery failed ({ErrorType}).", ex.GetType().Name);
        }

        var method = purpose == AuthenticationChallengePurpose.RegistrationSms ? "sms" : "email";

        return new(challenge.Id, method, Mask(destination), challenge.ExpiresAt, now.AddSeconds(30));
    }

    private async Task<(AuthenticationChallenge Challenge, User User)> GetChallengeAsync(
        Guid id,
        bool requireActive,
        CancellationToken ct)
    {
        var challenge = await db.AuthenticationChallenges.SingleOrDefaultAsync(item => item.Id == id, ct);

        if (challenge is null)
            throw new UnauthorizedAccessException(InvalidConfirmation);

        var challengeIsInvalid = !IsRegistration(challenge.Purpose)
            || challenge.ConsumedAt is not null
            || requireActive && !challenge.IsActive(DateTimeOffset.UtcNow)
            || string.IsNullOrWhiteSpace(challenge.DeliveryAddress);

        if (challengeIsInvalid)
            throw new UnauthorizedAccessException(InvalidConfirmation);

        var user = await db.Users
            .Include(existingUser => existingUser.Role)
            .SingleOrDefaultAsync(existingUser => existingUser.Id == challenge.UserId, ct);

        if (user is null)
            throw new UnauthorizedAccessException(InvalidConfirmation);

        var expectedAddress = challenge.Purpose == AuthenticationChallengePurpose.RegistrationSms
            ? user.Phone
            : user.Email;
        var userCannotCompleteRegistration = user.IsDeleted
            || user.Status != UserStatus.PendingActivation
            || user.Role?.Name != Roles.Customer
            || challenge.DeliveryAddress != expectedAddress;

        if (userCannotCompleteRegistration)
            throw new UnauthorizedAccessException(InvalidConfirmation);

        return (challenge, user);
    }

    private async Task RequireMethodAsync(string method, CancellationToken ct)
    {
        if (method is not ("email" or "sms"))
            throw new ArgumentException("Оберіть спосіб підтвердження: email або sms.");

        var availableMethods = await GetMethodsAsync(ct);

        if (!availableMethods.Methods.Contains(method))
        {
            var message = method == "sms"
                ? "SMS вимкнено або недоступні. Оберіть пошту."
                : "Підтвердження поштою тимчасово недоступне.";
            throw new InvalidOperationException(message);
        }
    }

    private async Task RequireSendLimitAsync(Guid? userId, string destination, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var recentChallenges = db.AuthenticationChallenges.Where(challenge =>
            (challenge.UserId == userId || challenge.DeliveryAddress == destination)
            && (challenge.Purpose == AuthenticationChallengePurpose.RegistrationEmail
                || challenge.Purpose == AuthenticationChallengePurpose.RegistrationSms)
            && challenge.LastCodeSentAt > now.AddHours(-1));

        var wasSentRecently = await recentChallenges
            .AnyAsync(challenge => challenge.LastCodeSentAt > now.AddSeconds(-30), ct);

        if (wasSentRecently)
            throw new InvalidOperationException("Зачекайте 30 секунд перед повторним надсиланням коду.");

        var hourlySendLimitReached = await recentChallenges.CountAsync(ct) >= 5;

        if (hourlySendLimitReached)
            throw new InvalidOperationException("Ліміт надсилання кодів вичерпано. Спробуйте через годину.");
    }

    private string Hash(Guid id, string code)
    {
        var key = Convert.FromBase64String(security.Value.OtpHashKey);

        if (key.Length < 32)
            throw new InvalidOperationException("Security:OtpHashKey must decode to at least 32 bytes.");

        var message = Encoding.UTF8.GetBytes($"{id:N}:{code}");
        var hash = HMACSHA256.HashData(key, message);
        return Convert.ToBase64String(hash);
    }

    private static string Mask(string destination)
    {
        var at = destination.IndexOf('@');

        if (at > 0)
            return destination[..Math.Min(2, at)] + "***" + destination[at..];

        return "+" + destination[..3] + "*****" + destination[^2..];
    }
}
