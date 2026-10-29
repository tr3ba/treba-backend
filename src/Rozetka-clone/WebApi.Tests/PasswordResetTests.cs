using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities;
using Domain.Entities.Users;
using Infrastructure.Authentication;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace WebApi.Tests;

public sealed class PasswordResetTests
{
    private const string OldPassword = "OriginalPassword123!";
    private const string NewPassword = "ReplacementPassword123!";
    private static readonly IOptions<SecurityOptions> Security = Options.Create(new SecurityOptions
    {
        EncryptionKey = Convert.ToBase64String(new byte[32]),
        OtpHashKey = Convert.ToBase64String(Enumerable.Repeat((byte)11, 32).ToArray()),
    });

    private sealed class Email : IEmailSender
    {
        public string? Code;
        public int Sends;
        public bool Fail;
        public Task SendSecurityCodeAsync(string recipient, string displayName, string code, TimeSpan validFor, CancellationToken cancellationToken = default)
        {
            Sends++; Code = code;
            if (Fail) throw new System.Net.Mail.SmtpException("fixture failure");
            return Task.CompletedTask;
        }
    }
    private sealed class Sms : ISmsSender
    {
        public bool IsConfigured => true;
        public string? Code;
        public string? Recipient;
        public int Sends;
        public Task SendAsync(string recipient, string text, Guid requestId, CancellationToken ct = default)
        {
            Sends++; Recipient = recipient; Code = Regex.Match(text, @"\b[0-9]{6}\b").Value;
            return Task.CompletedTask;
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly DbContextOptions<ApplicationDbContext> Options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public readonly ApplicationDbContext Db;
        public readonly Email Email = new();
        public readonly Sms Sms = new();
        public readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "password-reset-fixture-secret-at-least-32-bytes",
            ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test",
        }).Build();
        public Fixture() { Db = new(Options); }
        public IRegistrationService Methods => new RegistrationService(Db, new TokenService(Config), Email, Sms, Security,
            Microsoft.Extensions.Options.Options.Create(new SmtpEmailOptions { Enabled = true, Host = "fixture", FromAddress = "fixture@example.test" }));
        public PasswordResetService Service => new(Db, Methods, Email, Sms, Security, NullLogger<PasswordResetService>.Instance);
        public IdentityService Identity => new(Db, new TokenService(Config), new SecondFactorService(Db, Email, new(Security), Security), Config, new HttpClient());
        public async Task<User> Seed(string method = "email")
        {
            var role = Role.Create(Roles.Customer, Roles.Customer); Db.Roles.Add(role);
            var user = User.Create(Guid.NewGuid(), "customer@example.test", "380671234567", "Test", "Customer");
            user.AssignRole(role.Id); user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(OldPassword));
            if (method == "sms") user.VerifyPhone(); else user.VerifyEmail();
            user.SetRefreshToken("existing-refresh-token", DateTimeOffset.UtcNow.AddDays(1));
            Db.Users.Add(user); Db.FeatureFlags.Add(new() { Key = FeatureFlag.SmsSending, Enabled = true });
            await Db.SaveChangesAsync(); return user;
        }
        public async Task AgeSends()
        {
            foreach (var challenge in await Db.AuthenticationChallenges.ToListAsync())
                if (challenge.LastCodeSentAt is not null)
                    Db.Entry(challenge).Property(x => x.LastCodeSentAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-2);
            await Db.SaveChangesAsync();
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Theory]
    [InlineData("email")]
    [InlineData("sms")]
    public async Task Reset_RequiresCode_RevokesOldSessionsAndChallenges_AndPreservesSecondFactor(string method)
    {
        await using var f = new Fixture(); var user = await f.Seed(method);
        user.BeginAuthenticatorSetup(new AesSecretProtector(Security).Protect("JBSWY3DPEHPK3PXP")); user.EnableAuthenticator();
        user.SetRefreshToken("existing-refresh-token", DateTimeOffset.UtcNow.AddDays(1));
        var oldSession = SessionTokenBinding.Create(user);
        var login = AuthenticationChallenge.Create(user.Id, AuthenticationChallengePurpose.Login, DateTimeOffset.UtcNow.AddMinutes(5));
        var admin = AuthenticationChallenge.Create(user.Id, AuthenticationChallengePurpose.AdminEntry, DateTimeOffset.UtcNow.AddMinutes(5));
        f.Db.AuthenticationChallenges.AddRange(login, admin); await f.Db.SaveChangesAsync();
        var initialHash = user.PasswordHash;
        var pending = await f.Service.RequestAsync(new(" CUSTOMER@example.test ", method));
        var code = method == "sms" ? f.Sms.Code! : f.Email.Code!;
        Assert.Equal(initialHash, user.PasswordHash); Assert.Equal("existing-refresh-token", user.RefreshToken);
        Assert.Matches("^[0-9]{6}$", code);
        Assert.DoesNotContain(code, (await f.Db.AuthenticationChallenges.SingleAsync(x => x.Id == pending.ChallengeId)).CodeHash!);
        if (method == "sms") Assert.Equal(user.Phone, f.Sms.Recipient);
        await f.Service.CompleteAsync(new(pending.ChallengeId, code, NewPassword));
        Assert.True(BCrypt.Net.BCrypt.Verify(NewPassword, user.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify(OldPassword, user.PasswordHash));
        Assert.Null(user.RefreshToken); Assert.False(SessionTokenBinding.IsCurrent(oldSession, user));
        Assert.All(await f.Db.AuthenticationChallenges.ToListAsync(), x => Assert.NotNull(x.ConsumedAt));
        Assert.True(user.AuthenticatorEnabled);
        Assert.Equal(method == "email", user.EmailVerified); Assert.Equal(method == "sms", user.PhoneVerified);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(pending.ChallengeId, code, NewPassword)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Identity.RefreshTokenAsync(new("existing-refresh-token")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Identity.LoginAsync(new(user.Email, OldPassword)));
        var loginResult = await f.Identity.LoginAsync(new(user.Email, NewPassword));
        Assert.Null(loginResult.Authentication); Assert.Contains("authenticator", loginResult.Challenge!.Methods);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("unverified-email")]
    [InlineData("unverified-phone")]
    [InlineData("blocked")]
    [InlineData("deleted")]
    public async Task UnknownUnavailableOrUnverifiedAccounts_ReceiveGenericResponse_AndNoMessage(string kind)
    {
        await using var f = new Fixture(); var user = await f.Seed();
        var email = kind == "unknown" ? "unknown@example.test" : user.Email;
        if (kind == "unverified-email") { user.ChangeEmail("unverified@example.test"); email = user.Email; }
        if (kind == "blocked") user.Block();
        if (kind == "deleted") user.Delete(null);
        await f.Db.SaveChangesAsync();
        var response = await f.Service.RequestAsync(new(email, kind == "unverified-phone" ? "sms" : "email"));
        Assert.Equal(PasswordResetService.RequestMessage, response.Message);
        Assert.NotEqual(Guid.Empty, response.ChallengeId);
        Assert.Equal(0, f.Email.Sends); Assert.Equal(0, f.Sms.Sends); Assert.Empty(f.Db.AuthenticationChallenges);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(response.ChallengeId, "123456", NewPassword)));
        Assert.True(BCrypt.Net.BCrypt.Verify(OldPassword, user.PasswordHash));
    }

    [Fact]
    public async Task FiveWrongAttempts_ExpireCode_AndOtherPurposeOrExpiredChallengesCannotResetPassword()
    {
        await using var f = new Fixture(); var user = await f.Seed();
        var response = await f.Service.RequestAsync(new(user.Email, "email"));
        var code = f.Email.Code!;
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(response.ChallengeId, code == "000000" ? "111111" : "000000", NewPassword)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(response.ChallengeId, code, NewPassword)));
        await f.AgeSends();
        var renewed = await f.Service.RequestAsync(new(user.Email, "email"));
        var challenge = await f.Db.AuthenticationChallenges.SingleAsync(x => x.Id == renewed.ChallengeId);
        f.Db.Entry(challenge).Property(x => x.ExpiresAt).CurrentValue = DateTimeOffset.UtcNow.AddSeconds(-1);
        var unrelated = AuthenticationChallenge.Create(user.Id, AuthenticationChallengePurpose.RegistrationEmail, DateTimeOffset.UtcNow.AddMinutes(5));
        f.Db.AuthenticationChallenges.Add(unrelated); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(renewed.ChallengeId, f.Email.Code!, NewPassword)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(unrelated.Id, f.Email.Code!, NewPassword)));
        Assert.True(BCrypt.Net.BCrypt.Verify(OldPassword, user.PasswordHash));
    }

    [Fact]
    public async Task Resend_CooldownAndHourlyBudgetBlockAdditionalSends_AndNewChallengeInvalidatesOldCode()
    {
        await using var f = new Fixture(); var user = await f.Seed();
        var first = await f.Service.RequestAsync(new(user.Email, "email")); var firstCode = f.Email.Code!;
        var throttled = await f.Service.RequestAsync(new(user.Email, "email"));
        Assert.Equal(first.Message, throttled.Message); Assert.Equal(1, f.Email.Sends);
        await f.AgeSends();
        var latest = await f.Service.RequestAsync(new(user.Email, "email"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(first.ChallengeId, firstCode, NewPassword)));
        for (var i = 0; i < 3; i++) { await f.AgeSends(); latest = await f.Service.RequestAsync(new(user.Email, "email")); }
        await f.AgeSends();
        var overBudget = await f.Service.RequestAsync(new(user.Email, "email"));
        Assert.Equal(first.Message, overBudget.Message); Assert.Equal(5, f.Email.Sends);
        // The last delivered code remains usable when a further send is throttled.
        await f.Service.CompleteAsync(new(latest.ChallengeId, f.Email.Code!, NewPassword));
    }

    [Fact]
    public async Task SmsFeature_DisablesRecoverySends_ButPreviouslyDeliveredCodeCanStillBeConfirmed()
    {
        await using var f = new Fixture(); var user = await f.Seed("sms");
        var pending = await f.Service.RequestAsync(new(user.Email, "sms"));
        var flag = await f.Db.FeatureFlags.SingleAsync(); flag.Enabled = false; await f.Db.SaveChangesAsync();
        await f.AgeSends();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RequestAsync(new(user.Email, "sms")));
        Assert.Equal(1, f.Sms.Sends);
        await f.Service.CompleteAsync(new(pending.ChallengeId, f.Sms.Code!, NewPassword));
    }

    [Fact]
    public async Task CodesBecomeInvalidWhenPasswordOrDeliveryContactChanges()
    {
        await using var f = new Fixture(); var user = await f.Seed();
        var pending = await f.Service.RequestAsync(new(user.Email, "email")); var oldCode = f.Email.Code!;
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword("ChangedElsewhere123!")); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(pending.ChallengeId, oldCode, NewPassword)));
        await f.AgeSends();
        pending = await f.Service.RequestAsync(new(user.Email, "email"));
        user.ChangeEmail("changed@example.test"); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CompleteAsync(new(pending.ChallengeId, f.Email.Code!, NewPassword)));
        Assert.True(BCrypt.Net.BCrypt.Verify("ChangedElsewhere123!", user.PasswordHash));
    }

    [Fact]
    public async Task DeliveryFailureDoesNotChangePasswordOrRevealAccount_AndCountsAgainstCooldown()
    {
        await using var f = new Fixture(); var user = await f.Seed(); f.Email.Fail = true;
        var response = await f.Service.RequestAsync(new(user.Email, "email"));
        Assert.Equal(PasswordResetService.RequestMessage, response.Message);
        Assert.True(BCrypt.Net.BCrypt.Verify(OldPassword, user.PasswordHash));
        await f.Service.RequestAsync(new(user.Email, "email")); Assert.Equal(1, f.Email.Sends);
    }

    [Fact]
    public async Task AStalePasswordWriteCannotOverwriteACompletedReset()
    {
        await using var f = new Fixture(); var user = await f.Seed();
        await using var staleDb = new ApplicationDbContext(f.Options);
        var staleUser = await staleDb.Users.SingleAsync();
        var pending = await f.Service.RequestAsync(new(user.Email, "email"));
        await f.Service.CompleteAsync(new(pending.ChallengeId, f.Email.Code!, NewPassword));
        staleUser.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword("StaleConcurrentPassword123!"));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
        await using var freshDb = new ApplicationDbContext(f.Options);
        Assert.True(BCrypt.Net.BCrypt.Verify(NewPassword, (await freshDb.Users.SingleAsync()).PasswordHash));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("lowercase123")]
    [InlineData("UPPERCASE123")]
    [InlineData("WithoutDigits")]
    public async Task WeakPasswordsDoNotConsumeAValidCode(string password)
    {
        await using var f = new Fixture(); var user = await f.Seed();
        var pending = await f.Service.RequestAsync(new(user.Email, "email"));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.CompleteAsync(new(pending.ChallengeId, f.Email.Code!, password)));
        await f.Service.CompleteAsync(new(pending.ChallengeId, f.Email.Code!, NewPassword));
    }

    [Fact]
    public async Task HttpRecovery_RequiresMethodAndCode_AndReturnsNoAuthenticationTokens()
    {
        await using var f = new Fixture(); var user = await f.Seed();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IPasswordResetService>(f.Service); builder.Services.AddSingleton(f.Methods);
        builder.Services.AddSingleton<IIdentityService>(f.Identity);
        builder.Services.AddSingleton<ISecondFactorService>(new SecondFactorService(f.Db, f.Email, new(Security), Security));
        builder.Services.AddControllers().AddApplicationPart(typeof(WebApi.Controllers.AuthController).Assembly);
        builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<WebApi.Errors.ApiExceptionHandler>();
        builder.Services.AddRateLimiter(options => options.AddPolicy("registration", context =>
            System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("fixture", _ => new() { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        await using var app = builder.Build(); app.UseExceptionHandler(); app.UseRouting(); app.UseRateLimiter(); app.MapControllers(); await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email = user.Email })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/password-reset/request", new RequestPasswordResetRequest(user.Email, "email"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("accessToken", body); Assert.DoesNotContain("refreshToken", body);
        var pending = (await response.Content.ReadFromJsonAsync<PasswordResetChallengeResponse>())!;
        var completed = await client.PostAsJsonAsync("/api/auth/password-reset/complete", new CompletePasswordResetRequest(pending.ChallengeId, f.Email.Code!, NewPassword));
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode); Assert.Empty(await completed.Content.ReadAsStringAsync());
        Assert.Null(user.RefreshToken);
    }
}
