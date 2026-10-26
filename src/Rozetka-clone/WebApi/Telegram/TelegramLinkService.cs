using Application.Common;
using Contracts.Telegram;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace WebApi.Telegram;

public sealed class TelegramLinkService(ApplicationDbContext db, IOptions<TelegramOptions> options, TimeProvider clock)
{
    public static bool EligibleRole(string? role) => role is Roles.Administrator or Roles.Manager or Roles.Moderator or Roles.Seller;
    public async Task<User> RequireUserAsync(Guid userId, CancellationToken ct, bool allowCustomer = false)
    {
        var user = await db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active
            || !(EligibleRole(user.Role?.Name) || allowCustomer && user.Role?.Name == "Customer"))
            throw new UnauthorizedAccessException(allowCustomer
                ? "Для підключення Telegram потрібен активний обліковий запис Treba."
                : "Робочі інструменти Telegram доступні лише активним працівникам і продавцям Treba.");
        return user;
    }
    public TelegramConfigurationDto Configuration() => new(options.Value.Configured,
        options.Value.Configured ? options.Value.BotUsername : null, options.Value.Configured ? options.Value.MiniAppUrl : null);

    public async Task<TelegramLinkDto> StatusAsync(Guid userId, CancellationToken ct)
    {
        if (!options.Value.Configured) return new(false, false, null, null, null, null, null, null);
        await RequireUserAsync(userId, ct, allowCustomer: true);
        var link = await db.TelegramBindings.AsNoTracking().SingleOrDefaultAsync(b => b.UserId == userId, ct);
        var now = clock.GetUtcNow();
        var pending = await db.TelegramLinkChallenges.AsNoTracking().Where(c => c.UserId == userId && c.CompletedAt == null
            && c.ExpiresAt > now && c.TelegramUserId != null).OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        return new(true, link is not null, link?.Username, link?.TelegramUserId, link?.LinkedAt, pending?.Id, pending?.TelegramName, pending?.Username);
    }
    public async Task<TelegramLinkStartDto> BeginAsync(Guid userId, CancellationToken ct)
    {
        if (!options.Value.Configured) throw new BusinessRuleException("Адміністратор ще не підключив бота Treba.");
        await RequireUserAsync(userId, ct, allowCustomer: true);
        if (await db.TelegramBindings.AnyAsync(b => b.UserId == userId, ct)) throw new BusinessRuleException("Telegram уже підключено. Спочатку відключіть поточний обліковий запис.");
        var now = clock.GetUtcNow();
        foreach (var pending in await db.TelegramLinkChallenges.Where(c => c.UserId == userId && c.CompletedAt == null && c.ExpiresAt > now).ToListAsync(ct))
        { pending.ExpiresAt = now; pending.Version = Guid.NewGuid(); }
        var secret = TelegramSecrets.Create();
        var challenge = new TelegramLinkChallenge { UserId = userId, TokenHash = TelegramSecrets.Hash(secret), CreatedAt = now, ExpiresAt = now.AddMinutes(10) };
        db.TelegramLinkChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        return new(challenge.Id, "https://t.me/" + options.Value.BotUsername + "?start=link_" + secret, challenge.ExpiresAt);
    }
    // Called only for verified Bot API updates from a private chat. A website confirmation is still required.
    public async Task<TelegramUserDto> ClaimAsync(string secret, long telegramId, long chatId, string? username, string name, CancellationToken ct)
    {
        if (secret.Length != 43 || telegramId <= 0 || chatId != telegramId) throw new ArgumentException("Недійсне посилання підключення.");
        var hash = TelegramSecrets.Hash(secret); var now = clock.GetUtcNow();
        var challenge = await db.TelegramLinkChallenges.SingleOrDefaultAsync(c => c.TokenHash == hash, ct);
        if (challenge is null || challenge.ExpiresAt <= now || challenge.CompletedAt is not null
            || challenge.TelegramUserId is { } previous && previous != telegramId)
            throw new BusinessRuleException("Термін дії посилання минув або його вже використано. Створіть нове в профілі Treba.");
        var user = await RequireUserAsync(challenge.UserId, ct, allowCustomer: true);
        if (await db.TelegramBindings.AnyAsync(b => b.TelegramUserId == telegramId || b.UserId == user.Id, ct))
            throw new BusinessRuleException("Цей Telegram або профіль Treba вже підключено.");
        challenge.TelegramUserId = telegramId; challenge.ChatId = chatId;
        challenge.Username = username?[..Math.Min(100, username.Length)]; challenge.TelegramName = name[..Math.Min(200, name.Length)];
        challenge.ClaimedAt = now; challenge.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return new(user.Id, user.FirstName + " " + user.LastName, user.Role!.Name, telegramId);
    }
    public async Task<TelegramLinkDto> ConfirmAsync(Guid userId, Guid challengeId, CancellationToken ct)
    {
        await RequireUserAsync(userId, ct, allowCustomer: true); var now = clock.GetUtcNow();
        var challenge = await db.TelegramLinkChallenges.SingleOrDefaultAsync(c => c.Id == challengeId && c.UserId == userId, ct);
        if (challenge is null || challenge.ExpiresAt <= now || challenge.CompletedAt is not null || challenge.TelegramUserId is null || challenge.ChatId is null)
            throw new BusinessRuleException("Підтвердження Telegram недоступне. Почніть підключення знову.");
        if (await db.TelegramBindings.AnyAsync(b => b.TelegramUserId == challenge.TelegramUserId || b.UserId == userId, ct))
            throw new BusinessRuleException("Цей Telegram або профіль Treba вже підключено.");
        db.TelegramBindings.Add(new() { UserId = userId, TelegramUserId = challenge.TelegramUserId.Value,
            ChatId = challenge.ChatId.Value, Username = challenge.Username, LinkedAt = now });
        challenge.CompletedAt = now; challenge.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return await StatusAsync(userId, ct);
    }
    public async Task UnlinkAsync(Guid userId, CancellationToken ct)
    {
        await RequireUserAsync(userId, ct, allowCustomer: true);
        var binding = await db.TelegramBindings.SingleOrDefaultAsync(b => b.UserId == userId, ct);
        if (binding is not null) db.TelegramBindings.Remove(binding);
        var now = clock.GetUtcNow();
        foreach (var challenge in await db.TelegramLinkChallenges.Where(c => c.UserId == userId && c.CompletedAt == null && c.ExpiresAt > now).ToListAsync(ct))
        { challenge.ExpiresAt = now; challenge.Version = Guid.NewGuid(); }
        foreach (var guard in await db.TelegramGuardChallenges.Where(c => (c.ApprovedUserId == userId || db.AuthenticationChallenges.Any(a => a.Id == c.LoginChallengeId && a.UserId == userId)) && c.ConsumedAt == null && c.ExpiresAt > now).ToListAsync(ct))
        { guard.ExpiresAt = now; guard.Version = Guid.NewGuid(); }
        await db.SaveChangesAsync(ct);
    }
}
