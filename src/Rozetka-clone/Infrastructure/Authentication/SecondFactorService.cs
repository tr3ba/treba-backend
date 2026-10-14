using System.Security.Cryptography;
using System.Text;
using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QRCoder;

namespace Infrastructure.Authentication;

public sealed class SecondFactorService : ISecondFactorService
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailSender _emailSender;
    private readonly AesSecretProtector _protector;
    private readonly SecurityOptions _options;
    private readonly byte[] _hashKey;

    public SecondFactorService(
        IApplicationDbContext context,
        IEmailSender emailSender,
        AesSecretProtector protector,
        IOptions<SecurityOptions> options
    )
    {
        _context = context;
        _emailSender = emailSender;
        _protector = protector;
        _options = options.Value;
        try
        {
            _hashKey = Convert.FromBase64String(_options.OtpHashKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Security:OtpHashKey must be a base64-encoded key.",
                exception
            );
        }
        if (_hashKey.Length < 32)
        {
            throw new InvalidOperationException("Security:OtpHashKey must decode to at least 32 bytes.");
        }
    }

    public async Task<TwoFactorChallengeResponse?> CreateLoginChallengeAsync(
        User user,
        CancellationToken cancellationToken = default
    )
    {
        var methods = EnabledMethods(user);
        if (methods.Count == 0)
        {
            return null;
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(ChallengeLifetime);
        var challenge = AuthenticationChallenge.Create(
            user.Id,
            AuthenticationChallengePurpose.Login,
            expiresAt
        );
        _context.AuthenticationChallenges.Add(challenge);
        await _context.SaveChangesAsync(cancellationToken);
        return new(
            challenge.Id,
            methods,
            MaskEmail(user.Email),
            expiresAt
        );
    }

    public async Task SendLoginEmailCodeAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default
    )
    {
        var (challenge, user) = await GetActiveChallengeAsync(
            challengeId,
            AuthenticationChallengePurpose.Login,
            cancellationToken
        );
        if (!user.EmailTwoFactorEnabled)
        {
            throw new InvalidOperationException("Email codes are not enabled for this account.");
        }

        await SendCodeAsync(
            challenge,
            user,
            cancellationToken
        );
    }

    public async Task<User> VerifyLoginChallengeAsync(
        VerifyLoginSecondFactorRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var (challenge, user) = await GetActiveChallengeAsync(
            request.ChallengeId,
            AuthenticationChallengePurpose.Login,
            cancellationToken
        );
        var valid = request.Method switch
        {
            TwoFactorMethods.Authenticator when user.AuthenticatorEnabled => VerifyAuthenticator(
                user,
                request.Code
            ),
            TwoFactorMethods.Email when user.EmailTwoFactorEnabled => VerifyEmailCode(
                challenge,
                request.Code
            ),
            _ => false,
        };

        if (!valid)
        {
            challenge.RegisterFailure();
            await _context.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Invalid or expired security code.");
        }

        challenge.Consume();
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<AccountSecuritySettingsResponse> GetSettingsAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var user = await GetUserAsync(
            userId,
            cancellationToken
        );
        return ToSettings(user);
    }

    public async Task<AuthenticatorSetupResponse> BeginAuthenticatorSetupAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var user = await GetUserAsync(
            userId,
            cancellationToken
        );
        if (user.AuthenticatorEnabled)
        {
            throw new InvalidOperationException("Google Authenticator is already enabled.");
        }

        var secret = TotpUtility.GenerateSecret();
        user.BeginAuthenticatorSetup(_protector.Protect(secret));
        await _context.SaveChangesAsync(cancellationToken);

        var issuer = string.IsNullOrWhiteSpace(_options.Issuer)
            ? "TREBA"
            : _options.Issuer.Trim();
        var uri =
            $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";
        var png = PngByteQRCodeHelper.GetQRCode(
            uri,
            QRCodeGenerator.ECCLevel.Q,
            12
        );
        return new(
            secret,
            $"data:image/png;base64,{Convert.ToBase64String(png)}",
            user.Email,
            issuer
        );
    }

    public async Task<AccountSecuritySettingsResponse> ConfirmAuthenticatorAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var user = await GetUserAsync(
            userId,
            cancellationToken
        );
        if (!VerifyAuthenticator(
            user,
            code
        ))
        {
            throw new UnauthorizedAccessException("Invalid authenticator code.");
        }

        user.EnableAuthenticator();
        await _context.SaveChangesAsync(cancellationToken);
        return ToSettings(user);
    }

    public async Task<AccountSecuritySettingsResponse> DisableAuthenticatorAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var user = await GetUserAsync(
            userId,
            cancellationToken
        );
        if (!user.AuthenticatorEnabled)
        {
            throw new InvalidOperationException("Google Authenticator is not enabled.");
        }

        if (!VerifyAuthenticator(
            user,
            code
        ))
        {
            throw new UnauthorizedAccessException("Invalid authenticator code.");
        }

        user.DisableAuthenticator();
        await _context.SaveChangesAsync(cancellationToken);
        return ToSettings(user);
    }

    public async Task<StartEmailTwoFactorResponse> BeginEmailSetupAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var user = await GetUserAsync(
            userId,
            cancellationToken
        );
        if (user.EmailTwoFactorEnabled)
        {
            throw new InvalidOperationException("Email login codes are already enabled.");
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(ChallengeLifetime);
        var challenge = AuthenticationChallenge.Create(
            user.Id,
            AuthenticationChallengePurpose.EnableEmailTwoFactor,
            expiresAt
        );
        _context.AuthenticationChallenges.Add(challenge);
        await SendCodeAsync(
            challenge,
            user,
            cancellationToken
        );
        return new(
            challenge.Id,
            MaskEmail(user.Email),
            challenge.ExpiresAt
        );
    }

    public async Task<AccountSecuritySettingsResponse> ConfirmEmailSetupAsync(
        Guid userId,
        Guid challengeId,
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var (challenge, user) = await GetActiveChallengeAsync(
            challengeId,
            AuthenticationChallengePurpose.EnableEmailTwoFactor,
            cancellationToken
        );
        if (user.Id != userId)
        {
            throw new UnauthorizedAccessException("Security challenge does not belong to this account.");
        }

        if (!VerifyEmailCode(
            challenge,
            code
        ))
        {
            challenge.RegisterFailure();
            await _context.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Invalid or expired email code.");
        }
        challenge.Consume();
        user.EnableEmailTwoFactor();
        await _context.SaveChangesAsync(cancellationToken);
        return ToSettings(user);
    }

    public async Task<StartEmailTwoFactorResponse> BeginEmailDisableAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        var user = await GetUserAsync(
            userId,
            cancellationToken
        );
        if (!user.EmailTwoFactorEnabled)
        {
            throw new InvalidOperationException("Email login codes are not enabled.");
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(ChallengeLifetime);
        var challenge = AuthenticationChallenge.Create(
            user.Id,
            AuthenticationChallengePurpose.DisableEmailTwoFactor,
            expiresAt
        );
        _context.AuthenticationChallenges.Add(challenge);
        await SendCodeAsync(
            challenge,
            user,
            cancellationToken
        );
        return new(
            challenge.Id,
            MaskEmail(user.Email),
            challenge.ExpiresAt
        );
    }

    public async Task<AccountSecuritySettingsResponse> ConfirmEmailDisableAsync(
        Guid userId,
        Guid challengeId,
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var (challenge, user) = await GetActiveChallengeAsync(
            challengeId,
            AuthenticationChallengePurpose.DisableEmailTwoFactor,
            cancellationToken
        );
        if (user.Id != userId)
        {
            throw new UnauthorizedAccessException("Security challenge does not belong to this account.");
        }

        if (!VerifyEmailCode(
            challenge,
            code
        ))
        {
            challenge.RegisterFailure();
            await _context.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Invalid or expired email code.");
        }

        challenge.Consume();
        user.DisableEmailTwoFactor();
        await _context.SaveChangesAsync(cancellationToken);
        return ToSettings(user);
    }

    private async Task SendCodeAsync(
        AuthenticationChallenge challenge,
        User user,
        CancellationToken cancellationToken
    )
    {
        var now = DateTimeOffset.UtcNow;
        if (!challenge.CanSendCode(now))
        {
            throw new InvalidOperationException("Wait 30 seconds before requesting another code.");
        }

        var code = RandomNumberGenerator
            .GetInt32(
                0,
                1_000_000
            )
            .ToString(
                "D6",
                System.Globalization.CultureInfo.InvariantCulture
            );
        challenge.SetCode(
            HashCode(
                challenge.Id,
                code
            ),
            now.Add(ChallengeLifetime),
            now
        );
        await _context.SaveChangesAsync(cancellationToken);
        await _emailSender.SendSecurityCodeAsync(
            user.Email,
            DisplayName(user),
            code,
            ChallengeLifetime,
            cancellationToken
        );
    }

    private async Task<(AuthenticationChallenge Challenge, User User)> GetActiveChallengeAsync(
        Guid challengeId,
        AuthenticationChallengePurpose purpose,
        CancellationToken cancellationToken
    )
    {
        var challenge =
            await _context.AuthenticationChallenges.FirstOrDefaultAsync(
                item => item.Id == challengeId,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("Security challenge was not found.");
        if (challenge.Purpose != purpose
            || !challenge.IsActive(DateTimeOffset.UtcNow))
        {
            throw new UnauthorizedAccessException("Security challenge has expired.");
        }

        var user =
            await _context
                .Users
                .Include(item => item.Role)
                .FirstOrDefaultAsync(
                    item => item.Id == challenge.UserId,
                    cancellationToken
                )
            ?? throw new UnauthorizedAccessException("Account was not found.");
        if (user.Status is UserStatus.Blocked or UserStatus.Deleted)
        {
            throw new UnauthorizedAccessException("Account is unavailable.");
        }

        return (challenge, user);
    }

    private async Task<User> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken
    )
    {
        return await _context
                .Users
            .Include(item => item.Role)
            .FirstOrDefaultAsync(
                item => item.Id == userId,
                cancellationToken
            )
            ?? throw new InvalidOperationException("Account was not found.");
    }

    private bool VerifyAuthenticator(
        User user,
        string code
    )
    {
        if (string.IsNullOrWhiteSpace(user.AuthenticatorSecretProtected))
        {
            return false;
        }

        return TotpUtility.Validate(
            _protector.Unprotect(user.AuthenticatorSecretProtected),
            code.Trim(),
            DateTimeOffset.UtcNow
        );
    }

    private bool VerifyEmailCode(
        AuthenticationChallenge challenge,
        string code
    )
    {
        if (string.IsNullOrWhiteSpace(challenge.CodeHash))
        {
            return false;
        }

        var expected = Convert.FromBase64String(challenge.CodeHash);
        var actual = Convert.FromBase64String(
            HashCode(
                challenge.Id,
                code.Trim()
            )
        );
        return CryptographicOperations.FixedTimeEquals(
            expected,
            actual
        );
    }

    private string HashCode(
        Guid challengeId,
        string code
    )
    {
        using var hmac = new HMACSHA256(_hashKey);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));
    }

    private static IReadOnlyList<string> EnabledMethods(
        User user
    )
    {
        var methods = new List<string>(2);
        if (user.AuthenticatorEnabled)
        {
            methods.Add(TwoFactorMethods.Authenticator);
        }

        if (user.EmailTwoFactorEnabled)
        {
            methods.Add(TwoFactorMethods.Email);
        }

        return methods;
    }

    private static AccountSecuritySettingsResponse ToSettings(
        User user
    )
    {
        return new(
            user.AuthenticatorEnabled,
            user.AuthenticatorEnabledAt,
            user.EmailVerified,
            user.EmailTwoFactorEnabled,
            MaskEmail(user.Email)
        );
    }

    private static string MaskEmail(
        string email
    )
    {
        var parts = email.Split(
            '@',
            2
        );
        if (parts.Length != 2)
        {
            return "***";
        }

        var local = parts[0];
        var visible = local.Length <= 2
            ? local[..1]
            : local[..2];
        return $"{visible}***@{parts[1]}";
    }

    private static string DisplayName(
        User user
    )
    {
        return string.IsNullOrWhiteSpace($"{user.FirstName} {user.LastName}".Trim())
            ? user.Email
            : $"{user.FirstName} {user.LastName}".Trim();
    }

    private TimeSpan ChallengeLifetime
    {
        get
        {
            return TimeSpan.FromMinutes(
                Math.Clamp(
                    _options.ChallengeLifetimeMinutes,
                    2,
                    15
                )
            );
        }
    }
}
