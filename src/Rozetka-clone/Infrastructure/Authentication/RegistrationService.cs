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

namespace Infrastructure.Authentication;

public sealed class RegistrationService(
    IApplicationDbContext db, ITokenService tokens, IEmailSender email, ISmsSender sms,
    IOptions<SecurityOptions> security, IOptions<SmtpEmailOptions> smtp) : IRegistrationService
{
    private static bool IsRegistration(AuthenticationChallengePurpose purpose) =>
        purpose is AuthenticationChallengePurpose.RegistrationEmail or AuthenticationChallengePurpose.RegistrationSms;

    public async Task<RegistrationMethodsResponse> GetMethodsAsync(CancellationToken ct = default)
    {
        var methods = new List<string>();
        if (smtp.Value.Enabled && !string.IsNullOrWhiteSpace(smtp.Value.Host) && !string.IsNullOrWhiteSpace(smtp.Value.FromAddress))
            methods.Add("email");
        if (sms.IsConfigured && await db.FeatureFlags.AsNoTracking().AnyAsync(x => x.Key == FeatureFlag.SmsSending && x.Enabled, ct))
            methods.Add("sms");
        return new(methods);
    }

    public async Task<RegistrationChallengeResponse> BeginAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), null, true))
            throw new ArgumentException("Перевірте дані та обов'язково оберіть спосіб підтвердження: email або sms.");
        if (!string.IsNullOrWhiteSpace(request.RoleName) && request.RoleName != Roles.Customer)
            throw new InvalidOperationException("Public registration only supports the Customer role.");
        var method = request.VerificationMethod!;
        await RequireMethodAsync(method, ct);
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : TurboSmsSender.NormalizePhone(request.PhoneNumber);
        if (method == "sms" && phone is null) throw new ArgumentException("Для SMS потрібен номер телефону.");
        var destination = method == "sms" ? phone! : normalizedEmail;
        var purpose = method == "sms" ? AuthenticationChallengePurpose.RegistrationSms : AuthenticationChallengePurpose.RegistrationEmail;
        var user = await db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Email == normalizedEmail, ct);
        if (user is not null && (user.Status != UserStatus.PendingActivation || user.IsDeleted || user.Role?.Name != Roles.Customer
            || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)))
            throw new InvalidOperationException("Ця електронна адреса вже використовується.");
        await RequireSendLimitAsync(user?.Id, destination, ct);
        if (user is null)
        {
            var role = await db.Roles.SingleOrDefaultAsync(x => x.Name == Roles.Customer, ct)
                ?? throw new InvalidOperationException("Customer role is missing.");
            user = User.Create(Guid.NewGuid(), normalizedEmail, phone, request.FirstName, request.LastName);
            user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(request.Password));
            user.AssignRole(role.Id);
            db.Users.Add(user);
        }
        else user.UpdateProfile(request.FirstName, request.LastName, null, phone);
        return await SendAsync(user, purpose, destination, ct);
    }

    public async Task<RegistrationChallengeResponse> ResendAsync(Guid challengeId, CancellationToken ct = default)
    {
        var (challenge, user) = await GetChallengeAsync(challengeId, requireActive: false, ct);
        var method = challenge.Purpose == AuthenticationChallengePurpose.RegistrationSms ? "sms" : "email";
        await RequireMethodAsync(method, ct);
        await RequireSendLimitAsync(user.Id, challenge.DeliveryAddress!, ct);
        return await SendAsync(user, challenge.Purpose, challenge.DeliveryAddress!, ct);
    }

    public async Task<AuthResponse> VerifyAsync(VerifyRegistrationRequest request, CancellationToken ct = default)
    {
        var (challenge, user) = await GetChallengeAsync(request.ChallengeId, requireActive: true, ct);
        if (request.Code is null || request.Code.Length != 6 || request.Code.Any(c => c is < '0' or > '9')
            || string.IsNullOrEmpty(challenge.CodeHash)
            || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(challenge.CodeHash), Convert.FromBase64String(Hash(challenge.Id, request.Code))))
        {
            challenge.RegisterFailure();
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Невірний або прострочений код підтвердження.");
        }
        if (challenge.Purpose == AuthenticationChallengePurpose.RegistrationSms) user.VerifyPhone();
        else user.VerifyEmail();
        challenge.Consume();
        user.RegisterLogin();
        var refreshToken = tokens.GenerateRefreshToken();
        user.SetRefreshToken(refreshToken, DateTimeOffset.UtcNow.AddDays(7));
        // Activation, challenge consumption and refresh token commit together; concurrent replay fails.
        await db.SaveChangesAsync(ct);
        var role = user.Role?.Name ?? Roles.Customer;
        return new(user.Id, user.Email, user.FirstName, user.LastName, role,
            tokens.GenerateAccessToken(user, role), refreshToken, tokens.GetAccessTokenExpiration());
    }

    private async Task<RegistrationChallengeResponse> SendAsync(User user, AuthenticationChallengePurpose purpose, string destination, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(security.Value.ChallengeLifetimeMinutes, 2, 15));
        var previous = await db.AuthenticationChallenges.Where(x => x.UserId == user.Id && x.ConsumedAt == null
            && (x.Purpose == AuthenticationChallengePurpose.RegistrationEmail || x.Purpose == AuthenticationChallengePurpose.RegistrationSms)).ToListAsync(ct);
        foreach (var old in previous) old.Consume();
        var challenge = AuthenticationChallenge.Create(user.Id, purpose, now.Add(lifetime));
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        challenge.SetDeliveryAddress(destination);
        challenge.SetCode(Hash(challenge.Id, code), now.Add(lifetime), now);
        db.AuthenticationChallenges.Add(challenge);
        // Reserve the send and invalidate old codes before calling a paid service. Failures count toward limits.
        await db.SaveChangesAsync(ct);
        if (purpose == AuthenticationChallengePurpose.RegistrationSms)
            await sms.SendAsync(destination, $"TREBA: kod reiestratsii {code}. Diie {lifetime.TotalMinutes:0} khv.", challenge.Id, ct);
        else await email.SendSecurityCodeAsync(destination, $"{user.FirstName} {user.LastName}", code, lifetime, ct);
        return new(challenge.Id, purpose == AuthenticationChallengePurpose.RegistrationSms ? "sms" : "email",
            Mask(destination), challenge.ExpiresAt, now.AddSeconds(30));
    }

    private async Task<(AuthenticationChallenge Challenge, User User)> GetChallengeAsync(Guid id, bool requireActive, CancellationToken ct)
    {
        var challenge = await db.AuthenticationChallenges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (challenge is null || !IsRegistration(challenge.Purpose) || challenge.ConsumedAt is not null
            || requireActive && !challenge.IsActive(DateTimeOffset.UtcNow) || string.IsNullOrWhiteSpace(challenge.DeliveryAddress))
            throw new UnauthorizedAccessException("Підтвердження недійсне. Почніть реєстрацію ще раз.");
        var user = await db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == challenge.UserId, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.PendingActivation || user.Role?.Name != Roles.Customer
            || challenge.DeliveryAddress != (challenge.Purpose == AuthenticationChallengePurpose.RegistrationSms ? user.Phone : user.Email))
            throw new UnauthorizedAccessException("Підтвердження недійсне. Почніть реєстрацію ще раз.");
        return (challenge, user);
    }

    private async Task RequireMethodAsync(string method, CancellationToken ct)
    {
        if (method is not ("email" or "sms")) throw new ArgumentException("Оберіть спосіб підтвердження: email або sms.");
        if (!(await GetMethodsAsync(ct)).Methods.Contains(method))
            throw new InvalidOperationException(method == "sms" ? "SMS вимкнено або недоступні. Оберіть пошту." : "Підтвердження поштою тимчасово недоступне.");
    }

    private async Task RequireSendLimitAsync(Guid? userId, string destination, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var recent = db.AuthenticationChallenges.Where(x =>
            (x.UserId == userId || x.DeliveryAddress == destination)
            && (x.Purpose == AuthenticationChallengePurpose.RegistrationEmail || x.Purpose == AuthenticationChallengePurpose.RegistrationSms
                || x.Purpose == AuthenticationChallengePurpose.PasswordResetEmail || x.Purpose == AuthenticationChallengePurpose.PasswordResetSms)
            && x.LastCodeSentAt > now.AddHours(-1));
        if (await recent.AnyAsync(x => x.LastCodeSentAt > now.AddSeconds(-30), ct))
            throw new InvalidOperationException("Зачекайте 30 секунд перед повторним надсиланням коду.");
        if (await recent.CountAsync(ct) >= 5)
            throw new InvalidOperationException("Ліміт надсилання кодів вичерпано. Спробуйте через годину.");
    }

    private string Hash(Guid id, string code)
    {
        var key = Convert.FromBase64String(security.Value.OtpHashKey);
        if (key.Length < 32) throw new InvalidOperationException("Security:OtpHashKey must decode to at least 32 bytes.");
        return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id:N}:{code}")));
    }

    private static string Mask(string destination)
    {
        var at = destination.IndexOf('@');
        return at > 0 ? destination[..Math.Min(2, at)] + "***" + destination[at..]
            : "+" + destination[..3] + "*****" + destination[^2..];
    }
}
