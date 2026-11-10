using System.Globalization;
using System.Security.Cryptography;
using Application.Abstractions;
using Application.Common;
using Contracts.Authentication;
using Contracts.Telegram;
using Domain.Entities.Telegram;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QRCoder;

namespace WebApi.Telegram;

public sealed class TelegramGuardService(ApplicationDbContext db, TelegramLinkService links,
    IIdentityService identity, IOptions<TelegramOptions> options, TimeProvider clock)
{
    public async Task<GuardStartDto> CreateAsync(string? deviceName, string? clientIp, CancellationToken ct, Guid? loginChallengeId = null)
    {
        if (!options.Value.Configured)
            throw new BusinessRuleException("Telegram Guard ще не підключено.");

        if (loginChallengeId is null)
            throw new UnauthorizedAccessException("Спочатку підтвердьте пароль або соціальний вхід цього облікового запису.");

        if (loginChallengeId is { } loginId)
        {
            var login = await db.AuthenticationChallenges.SingleOrDefaultAsync(c => c.Id == loginId, ct);

            if (login is null)
                throw new UnauthorizedAccessException("Термін дії запиту входу минув або Guard не підключено.");

            var isLoginChallenge = login.Purpose == Domain.Entities.Users.AuthenticationChallengePurpose.Login;
            var loginIsActive = login.IsActive(clock.GetUtcNow());

            if (!isLoginChallenge || !loginIsActive)
                throw new UnauthorizedAccessException("Термін дії запиту входу минув або Guard не підключено.");

            var hasTelegramBindingAtLogin = await db.TelegramBindings.AnyAsync(b =>
                b.UserId == login.UserId
                && b.LinkedAt <= login.CreatedAt, ct);

            if (!hasTelegramBindingAtLogin)
                throw new UnauthorizedAccessException("Термін дії запиту входу минув або Guard не підключено.");

            await links.RequireUserAsync(login.UserId, ct);
        }

        var browser = TelegramSecrets.Create();
        var approval = TelegramSecrets.Create();
        var now = clock.GetUtcNow();
        var challenge = new TelegramGuardChallenge
        {
            LoginChallengeId = loginChallengeId,
            BrowserSecretHash = TelegramSecrets.Hash(browser),
            ApprovalSecretHash = TelegramSecrets.Hash(approval),
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Веб-панель Treba" : deviceName[..Math.Min(200, deviceName.Length)],
            ClientIp = clientIp is { Length: <= 64 } ? clientIp : "",
            DisplayCode = RandomNumberGenerator.GetInt32(100000, 1000000),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(2)
        };
        db.TelegramGuardChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        var payload = "treba-guard:v1:" + challenge.Id.ToString("N") + ":" + approval;
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        return new(challenge.Id, browser, payload, "data:image/png;base64," + Convert.ToBase64String(qr.GetGraphic(6)), challenge.DisplayCode.ToString(CultureInfo.InvariantCulture), challenge.ExpiresAt);
    }

    public async Task<GuardStatusDto> StatusAsync(Guid id, string secret, CancellationToken ct)
    {
        var challenge = await db.TelegramGuardChallenges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

        if (challenge is null || !TelegramSecrets.Matches(secret, challenge.BrowserSecretHash))
            throw new UnauthorizedAccessException("Недійсний запит Guard.");

        return new(challenge.ExpiresAt <= clock.GetUtcNow() ? "expired" : challenge.ConsumedAt is not null ? "consumed" : challenge.ApprovedAt is not null ? "approved" : "waiting", challenge.ExpiresAt);
    }

    public async Task<GuardPreviewDto> PreviewAsync(string payload, Guid userId, CancellationToken ct)
    {
        await links.RequireUserAsync(userId, ct);
        var challenge = await ScanAsync(payload, ct);
        await CheckLoginOwner(challenge, userId, ct);
        return new(challenge.Id, challenge.DeviceName, challenge.DisplayCode.ToString(CultureInfo.InvariantCulture), challenge.ExpiresAt);
    }

    public async Task ApproveAsync(string payload, Guid userId, long telegramId, CancellationToken ct)
    {
        await links.RequireUserAsync(userId, ct);

        if (!await db.TelegramBindings.AnyAsync(b => b.UserId == userId && b.TelegramUserId == telegramId, ct))
            throw new UnauthorizedAccessException();

        var challenge = await ScanAsync(payload, ct);
        await CheckLoginOwner(challenge, userId, ct);

        if (challenge.ApprovedAt is not null)
            throw new BusinessRuleException("Цей QR-код уже підтверджено.");
        challenge.ApprovedUserId = userId;
        challenge.ApprovedTelegramUserId = telegramId;
        challenge.ApprovedAt = clock.GetUtcNow();
        challenge.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }

    public async Task<LoginResponse> CompleteAsync(Guid id, string secret, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var challenge = await db.TelegramGuardChallenges.SingleOrDefaultAsync(c => c.Id == id, ct);

        if (challenge is null)
            throw new UnauthorizedAccessException("Вхід через Guard не підтверджено, уже завершено або термін дії минув.");

        var browserSecretIsValid = TelegramSecrets.Matches(secret, challenge.BrowserSecretHash);
        var challengeHasExpired = challenge.ExpiresAt <= clock.GetUtcNow();
        var challengeWasConsumed = challenge.ConsumedAt is not null;
        var challengeWasNotApproved = challenge.ApprovedAt is null || challenge.ApprovedUserId is null;

        if (!browserSecretIsValid || challengeHasExpired || challengeWasConsumed || challengeWasNotApproved)
            throw new UnauthorizedAccessException("Вхід через Guard не підтверджено, уже завершено або термін дії минув.");

        var user = await links.RequireUserAsync(challenge.ApprovedUserId.Value, ct);
        await CheckLoginOwner(challenge, user.Id, ct);
        var bindingWasActiveAtApproval = await db.TelegramBindings.AnyAsync(b =>
            b.UserId == user.Id
            && b.TelegramUserId == challenge.ApprovedTelegramUserId
            && b.LinkedAt <= challenge.ApprovedAt, ct);

        if (!bindingWasActiveAtApproval)
            throw new UnauthorizedAccessException("Підключення Telegram скасовано.");

        challenge.ConsumedAt = clock.GetUtcNow();
        challenge.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct); // The concurrency token makes completion single use.
        var result = challenge.LoginChallengeId is { } loginId
            ? new LoginResponse(await identity.CompleteSecondFactorLoginAsync(new(loginId, TwoFactorMethods.Telegram, challenge.DisplayCode.ToString(CultureInfo.InvariantCulture)), ct), null)
            : await identity.LoginWithGuardAsync(user.Id, ct);

        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return result;
    }

    private async Task CheckLoginOwner(TelegramGuardChallenge guard, Guid userId, CancellationToken ct)
    {
        if (guard.LoginChallengeId is not { } loginId)
            throw new UnauthorizedAccessException("Спочатку підтвердьте пароль або соціальний вхід цього облікового запису.");

        var login = await db.AuthenticationChallenges.SingleOrDefaultAsync(c => c.Id == loginId, ct);

        if (login is null)
            throw new UnauthorizedAccessException("Підтвердьте вхід через Telegram того самого облікового запису.");

        var belongsToSameUser = login.UserId == userId;
        var isLoginChallenge = login.Purpose == Domain.Entities.Users.AuthenticationChallengePurpose.Login;
        var loginIsActive = login.IsActive(clock.GetUtcNow());

        if (!belongsToSameUser || !isLoginChallenge || !loginIsActive)
            throw new UnauthorizedAccessException("Підтвердьте вхід через Telegram того самого облікового запису.");

        var hadTelegramBindingWhenGuardStarted = await db.TelegramBindings.AnyAsync(b =>
            b.UserId == userId
            && b.LinkedAt <= guard.CreatedAt, ct);

        if (!hadTelegramBindingWhenGuardStarted)
            throw new UnauthorizedAccessException("Підтвердьте вхід через Telegram того самого облікового запису.");
    }

    private async Task<TelegramGuardChallenge> ScanAsync(string payload, CancellationToken ct)
    {
        var parts = payload.Split(':');

        if (parts.Length != 4)
            throw new ArgumentException("Це не QR-код входу Treba Guard.");

        var hasExpectedPrefix = parts[0] == "treba-guard" && parts[1] == "v1";
        var hasChallengeId = Guid.TryParseExact(parts[2], "N", out var id);

        if (!hasExpectedPrefix || !hasChallengeId)
            throw new ArgumentException("Це не QR-код входу Treba Guard.");

        var challenge = await db.TelegramGuardChallenges.SingleOrDefaultAsync(c => c.Id == id, ct);

        if (challenge is null)
            throw new BusinessRuleException("Термін дії QR-коду минув або його вже використано. Оновіть його на сторінці входу.");

        var approvalSecretIsValid = TelegramSecrets.Matches(parts[3], challenge.ApprovalSecretHash);
        var challengeHasExpired = challenge.ExpiresAt <= clock.GetUtcNow();
        var challengeWasConsumed = challenge.ConsumedAt is not null;

        if (!approvalSecretIsValid || challengeHasExpired || challengeWasConsumed)
            throw new BusinessRuleException("Термін дії QR-коду минув або його вже використано. Оновіть його на сторінці входу.");
        return challenge;
    }
}
