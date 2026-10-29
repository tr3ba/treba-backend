using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions;
using Application.Administration;
using Contracts.Authentication;
using Domain.Entities;
using Domain.Entities.Users;
using Infrastructure.Authentication;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebApi.Tests;

public sealed class RegistrationTests
{
    private static readonly IOptions<SecurityOptions> Security = Options.Create(new SecurityOptions
    {
        EncryptionKey = Convert.ToBase64String(new byte[32]),
        OtpHashKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()),
    });
    private sealed class Email : IEmailSender
    {
        public string? Code;
        public int Sends;
        public bool Fail;
        public Task SendSecurityCodeAsync(string recipient, string displayName, string code, TimeSpan validFor, CancellationToken cancellationToken = default)
        {
            Sends++; Code = code;
            if (Fail) throw new InvalidOperationException("Email unavailable");
            return Task.CompletedTask;
        }
    }
    private sealed class Provider : HttpMessageHandler
    {
        public int Sends;
        public string Reply = """{"response_code":800,"response_result":[{"response_code":0,"message_id":"fixture-message"}]}""";
        public string? Code;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sends++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.turbosms.ua/message/send.json", request.RequestUri!.ToString());
            Assert.Equal("Bearer test-provider-token", request.Headers.Authorization!.ToString());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("MAGAZIN", json.RootElement.GetProperty("sms").GetProperty("sender").GetString());
            Assert.Equal("380671234567", json.RootElement.GetProperty("recipients")[0].GetString());
            Assert.True(Guid.TryParseExact(json.RootElement.GetProperty("sequence_id").GetString(), "N", out _));
            Code = json.RootElement.GetProperty("sms").GetProperty("text").GetString()!.Split(' ')[3].TrimEnd('.');
            return new(Status) { Content = new StringContent(Reply) };
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly ApplicationDbContext Db = TelegramTestData.Memory();
        public readonly Email Email = new();
        public readonly Provider Provider = new();
        public readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "registration-tests-only-secret-at-least-32-bytes",
            ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test",
        }).Build();
        public readonly IOptions<SmtpEmailOptions> Smtp = Options.Create(new SmtpEmailOptions { Enabled = true, Host = "fixture", FromAddress = "fixture@example.test" });
        public TurboSmsSender Sms => new(new HttpClient(Provider), Db, Options.Create(new TurboSmsOptions { Token = "test-provider-token" }));
        public RegistrationService Service => new(Db, new TokenService(Config), Email, Sms, Security, Smtp);
        public IdentityService Identity => new(Db, new TokenService(Config), new SecondFactorService(Db, Email, new(Security), Security), Config, new HttpClient());
        public async Task Seed(bool sms = false)
        {
            Db.Roles.Add(Role.Create(Roles.Customer, Roles.Customer));
            if (sms) Db.FeatureFlags.Add(new() { Key = FeatureFlag.SmsSending, Enabled = true });
            await Db.SaveChangesAsync();
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private static RegisterRequest Request(string? method = "email", string? phone = "+380 (67) 123-45-67", string email = "new@example.test") =>
        new(email, "Password123!", "Олена", "Коваль", phone, VerificationMethod: method);

    [Fact]
    public async Task HttpRegistration_RequiresExplicitChoice_AndReturnsTokensOnlyAfterVerification()
    {
        await using var f = new Fixture(); await f.Seed();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IRegistrationService>(f.Service);
        builder.Services.AddSingleton<IIdentityService>(f.Identity);
        builder.Services.AddSingleton<ISecondFactorService>(new SecondFactorService(f.Db, f.Email, new(Security), Security));
        builder.Services.AddControllers().AddApplicationPart(typeof(WebApi.Controllers.AuthController).Assembly);
        builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<WebApi.Errors.ApiExceptionHandler>();
        builder.Services.AddRateLimiter(options => options.AddPolicy("registration", context =>
            System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("fixture", _ => new()
            {
                PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            })));
        await using var app = builder.Build();
        app.UseExceptionHandler(); app.UseRouting(); app.UseRateLimiter(); app.MapControllers(); await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", Request(null))).StatusCode);
        Assert.Empty(f.Db.Users); Assert.Equal(0, f.Email.Sends);
        var response = await client.PostAsJsonAsync("/api/auth/register", Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body); Assert.DoesNotContain("refreshToken", body);
        var pending = (await response.Content.ReadFromJsonAsync<RegistrationChallengeResponse>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/registration/verify", new VerifyRegistrationRequest(pending.ChallengeId, "invalid"))).StatusCode);
        var wrongCode = f.Email.Code == "000000" ? "111111" : "000000";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/registration/verify", new VerifyRegistrationRequest(pending.ChallengeId, wrongCode))).StatusCode);
        var confirmed = await client.PostAsJsonAsync("/api/auth/registration/verify", new VerifyRegistrationRequest(pending.ChallengeId, f.Email.Code!));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.NotEmpty((await confirmed.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("sms")]
    public async Task Registration_ActivatesOnlyAfterCode_VerifiesOnlyChosenContact_AndRejectsReplay(string method)
    {
        await using var f = new Fixture(); await f.Seed(sms: true);
        var pending = await f.Service.BeginAsync(Request(method));
        var user = await f.Db.Users.SingleAsync();
        Assert.Equal(UserStatus.PendingActivation, user.Status);
        Assert.False(user.EmailVerified); Assert.False(user.PhoneVerified); Assert.Null(user.RefreshToken);
        Assert.Equal(method, pending.Method);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Identity.LoginAsync(new(user.Email, "Password123!")));
        var code = method == "sms" ? f.Provider.Code! : f.Email.Code!;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.DoesNotContain(code, (await f.Db.AuthenticationChallenges.SingleAsync()).CodeHash!);
        var result = await f.Service.VerifyAsync(new(pending.ChallengeId, code));
        Assert.NotEmpty(result.AccessToken); Assert.NotEmpty(result.RefreshToken);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(method == "email", user.EmailVerified); Assert.Equal(method == "sms", user.PhoneVerified);
        Assert.Equal(method == "sms" ? 1 : 0, f.Provider.Sends);
        Assert.Equal(method == "email" ? 1 : 0, f.Email.Sends);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.VerifyAsync(new(pending.ChallengeId, code)));
        Assert.NotNull((await f.Identity.LoginAsync(new(user.Email, "Password123!"))).Authentication);
        if (method == "sms") { user.Block(); user.Unblock(); Assert.Equal(UserStatus.Active, user.Status); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("phone")]
    [InlineData("SMS")]
    public async Task MissingOrUnknownMethod_IsRejectedWithoutCreatingUserOrSending(string? method)
    {
        await using var f = new Fixture(); await f.Seed(true);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.BeginAsync(Request(method)));
        Assert.Empty(f.Db.Users); Assert.Equal(0, f.Provider.Sends); Assert.Equal(0, f.Email.Sends);
    }

    [Fact]
    public async Task MissingConfiguration_DoesNotOfferOrSendSms_AndMissingPhoneCannotCreateAnAccount()
    {
        await using var f = new Fixture(); await f.Seed(true);
        var unconfigured = new TurboSmsSender(new HttpClient(f.Provider), f.Db, Options.Create(new TurboSmsOptions()));
        var service = new RegistrationService(f.Db, new TokenService(f.Config), f.Email, unconfigured, Security, f.Smtp);
        Assert.DoesNotContain("sms", (await service.GetMethodsAsync()).Methods);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unconfigured.SendAsync("380671234567", "fixture", Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginAsync(Request("sms")));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.BeginAsync(Request("sms", phone: null)));
        Assert.Empty(f.Db.Users); Assert.Equal(0, f.Provider.Sends);
        f.Smtp.Value.Enabled = false;
        Assert.Empty((await service.GetMethodsAsync()).Methods);
    }

    [Fact]
    public async Task RegistrationCannotGrantStaffRolesOrOverwriteExistingCredentials()
    {
        await using var f = new Fixture(); await f.Seed();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.BeginAsync(Request() with { RoleName = Roles.Administrator }));
        var pending = await f.Service.BeginAsync(Request());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.BeginAsync(Request() with { Password = "DifferentPassword123!" }));
        Assert.Equal(1, f.Email.Sends);
        await f.Service.VerifyAsync(new(pending.ChallengeId, f.Email.Code!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.BeginAsync(Request()));
        Assert.Single(f.Db.Users); Assert.Equal(1, f.Email.Sends);
    }

    [Fact]
    public async Task SmsSwitch_DefaultsOff_Persists_AndBlocksResendAndProviderDirectly()
    {
        await using var f = new Fixture(); await f.Seed();
        Assert.Equal(new[] { "email" }, (await f.Service.GetMethodsAsync()).Methods);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.BeginAsync(Request("sms")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sms.SendAsync("380671234567", "fixture", Guid.NewGuid()));
        Assert.Empty(f.Db.Users); Assert.Equal(0, f.Provider.Sends);
        var actor = new OperationsTests.Actor(Guid.NewGuid(), Roles.Administrator);
        var settings = new FeatureSettingsService(f.Db, actor);
        var enabled = await settings.SetAsync(FeatureFlag.SmsSending, new(true, Guid.Empty), default);
        Assert.Contains("sms", (await f.Service.GetMethodsAsync()).Methods);
        var pending = await f.Service.BeginAsync(Request("sms"));
        await settings.SetAsync(FeatureFlag.SmsSending, new(false, enabled.Version), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResendAsync(pending.ChallengeId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sms.SendAsync("380671234567", "fixture", Guid.NewGuid()));
        Assert.Equal(1, f.Provider.Sends);
        // Disabling new sends must not invalidate a code already delivered.
        await f.Service.VerifyAsync(new(pending.ChallengeId, f.Provider.Code!));
    }

    [Fact]
    public async Task WrongCodes_ExpireAfterFiveAttempts_AndExpiredOrOtherPurposeCodesCannotActivate()
    {
        await using var f = new Fixture(); await f.Seed();
        var pending = await f.Service.BeginAsync(Request());
        var code = f.Email.Code!;
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.VerifyAsync(new(pending.ChallengeId, code == "000000" ? "111111" : "000000")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.VerifyAsync(new(pending.ChallengeId, code)));
        var user = await f.Db.Users.SingleAsync();
        var login = AuthenticationChallenge.Create(user.Id, AuthenticationChallengePurpose.Login, DateTimeOffset.UtcNow.AddMinutes(5));
        f.Db.AuthenticationChallenges.Add(login); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.VerifyAsync(new(login.Id, code)));
        Assert.Equal(UserStatus.PendingActivation, user.Status); Assert.Null(user.RefreshToken);
    }

    [Fact]
    public async Task Resend_RotatesCodeAndChallenge_EnforcesCooldownAndHourlyLimitAcrossAccounts()
    {
        await using var f = new Fixture(); await f.Seed(true);
        var pending = await f.Service.BeginAsync(Request("sms"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResendAsync(pending.ChallengeId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.BeginAsync(Request("sms", email: "other@example.test")));
        Assert.Equal(1, f.Provider.Sends);
        var oldCode = f.Provider.Code!;
        var previous = await f.Db.AuthenticationChallenges.SingleAsync();
        f.Db.Entry(previous).Property(x => x.LastCodeSentAt).CurrentValue = DateTimeOffset.UtcNow.AddSeconds(-31);
        f.Db.Entry(previous).Property(x => x.ExpiresAt).CurrentValue = DateTimeOffset.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.VerifyAsync(new(pending.ChallengeId, oldCode)));
        var renewed = await f.Service.ResendAsync(pending.ChallengeId);
        Assert.NotEqual(pending.ChallengeId, renewed.ChallengeId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.VerifyAsync(new(pending.ChallengeId, oldCode)));
        for (var i = 0; i < 3; i++)
        {
            foreach (var challenge in await f.Db.AuthenticationChallenges.ToListAsync())
                f.Db.Entry(challenge).Property(x => x.LastCodeSentAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-2);
            await f.Db.SaveChangesAsync();
            renewed = await f.Service.ResendAsync(renewed.ChallengeId);
        }
        foreach (var challenge in await f.Db.AuthenticationChallenges.ToListAsync())
            f.Db.Entry(challenge).Property(x => x.LastCodeSentAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-2);
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResendAsync(renewed.ChallengeId));
        Assert.Equal(5, f.Provider.Sends);
    }

    [Fact]
    public async Task FailedDelivery_LeavesPendingAccountRecoverable_AndNeverGrantsTokens()
    {
        await using var f = new Fixture(); await f.Seed();
        f.Email.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.BeginAsync(Request()));
        var user = await f.Db.Users.SingleAsync();
        Assert.Equal(UserStatus.PendingActivation, user.Status); Assert.Null(user.RefreshToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Identity.LoginAsync(new(user.Email, "Password123!")));
        var first = await f.Db.AuthenticationChallenges.SingleAsync();
        f.Db.Entry(first).Property(x => x.LastCodeSentAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync(); f.Email.Fail = false;
        var retry = await f.Service.BeginAsync(Request());
        await f.Service.VerifyAsync(new(retry.ChallengeId, f.Email.Code!));
        Assert.Single(f.Db.Users); Assert.Equal(UserStatus.Active, user.Status);
    }

    [Theory]
    [InlineData("{\"response_code\":0,\"response_result\":[{\"response_code\":406,\"message_id\":null}]}")]
    [InlineData("{\"response_code\":401,\"response_result\":null}")]
    [InlineData("{\"response_code\":0,\"response_result\":[]}")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"response_code\":\"0\",\"response_result\":[]}")]
    [InlineData("{\"response_code\":0,\"response_result\":[null]}")]
    [InlineData("{\"response_code\":0,\"response_result\":[{\"response_code\":\"0\",\"message_id\":\"fixture\"}]}")]
    public async Task TurboSms_Http200DoesNotHideProviderOrRecipientErrors(string body)
    {
        await using var f = new Fixture(); await f.Seed(true); f.Provider.Reply = body;
        await Assert.ThrowsAsync<SmsDeliveryException>(() => f.Service.BeginAsync(Request("sms")));
        Assert.Equal(1, f.Provider.Sends); Assert.Null((await f.Db.Users.SingleAsync()).RefreshToken);
    }

    [Theory]
    [InlineData("0671234567", "380671234567")]
    [InlineData("+380 (67) 123-45-67", "380671234567")]
    [InlineData("380671234567", "380671234567")]
    public void PhoneNumbers_NormalizeForProvider(string phone, string expected) => Assert.Equal(expected, TurboSmsSender.NormalizePhone(phone));

    [Theory]
    [InlineData("")]
    [InlineData("+38067123")]
    [InlineData("+380671234567 ext 9")]
    [InlineData("380+671234567")]
    public void InvalidPhoneNumbers_AreRejected(string phone) => Assert.Throws<ArgumentException>(() => TurboSmsSender.NormalizePhone(phone));
}
