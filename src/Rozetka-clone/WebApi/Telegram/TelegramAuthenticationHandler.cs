using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.Entities.Users;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace WebApi.Telegram;

public sealed class TelegramAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> schemes, ILoggerFactory logger,
    UrlEncoder encoder, IOptions<TelegramOptions> options, TelegramInitDataValidator validator, ApplicationDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    public const string SchemeName = "TelegramMiniApp";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.Authorization.ToString().StartsWith("Tma ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        if (!options.Value.Configured) return AuthenticateResult.Fail("Telegram is not configured.");
        try
        {
            var telegram = validator.Validate(Request.Headers.Authorization.ToString()[4..], options.Value.BotToken,
                TimeSpan.FromMinutes(Math.Clamp(options.Value.InitDataMaxAgeMinutes, 1, 60)));
            var binding = await db.TelegramBindings.AsNoTracking().SingleOrDefaultAsync(b => b.TelegramUserId == telegram.Id, Context.RequestAborted);
            if (binding is null || telegram.AuthenticatedAt.ToUnixTimeSeconds() < binding.LinkedAt.ToUnixTimeSeconds())
                return AuthenticateResult.Fail("Підключення Telegram не підтверджено або сеанс скасовано.");
            var user = await db.Users.AsNoTracking().Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == binding.UserId, Context.RequestAborted);
            if (user is null || user.Status != UserStatus.Active || user.IsDeleted || !TelegramLinkService.EligibleRole(user.Role?.Name))
                return AuthenticateResult.Fail("Цей обліковий запис не має доступу до Treba.");
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role!.Name),
                new Claim("firstName", user.FirstName), new Claim("lastName", user.LastName), new Claim("telegram_id", telegram.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
        }
        catch (UnauthorizedAccessException ex) { return AuthenticateResult.Fail(ex.Message); }
    }
}
