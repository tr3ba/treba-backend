using System.Net;
using System.Text.Json;
using Application.Abstractions;
using Contracts.Authentication;
using Domain.Entities.Users;
using Infrastructure.Authentication;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using WebApi.Controllers;

namespace WebApi.Tests;

public sealed class ExternalLoginTests
{
    private const string Origin = "https://treba.example";
    private const string Verifier = "fixture-verifier-abcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly IOptions<SecurityOptions> Security = Options.Create(new SecurityOptions
    {
        EncryptionKey = Convert.ToBase64String(new byte[32]),
        OtpHashKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()),
    });
    private sealed class Email : IEmailSender
    {
        public Task SendSecurityCodeAsync(string recipient, string displayName, string code, TimeSpan validFor, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Provider(string provider, string email = "customer@example.test", bool verified = true, bool missingSurname = false) : HttpMessageHandler
    {
        public int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/token") || request.RequestUri.AbsolutePath.EndsWith("/access_token"))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                var fields = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                    .Select(part => part.Split('=', 2)).ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1]));
                Assert.Equal(provider + "-client", fields["client_id"]);
                Assert.Equal(provider + "-fixture-secret", fields["client_secret"]);
                Assert.Equal(Origin + "/api/auth/external/" + provider + "/callback", fields["redirect_uri"]);
                if (provider == "google") Assert.Equal(Verifier, fields["code_verifier"]);
                if (fields["code"] == "invalid") return new(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"provider-token\"}") };
            }
            Assert.Equal("Bearer provider-token", request.Headers.Authorization!.ToString());
            var profile = provider == "google"
                ? JsonSerializer.Serialize(new { id = "google-subject", email, given_name = "Олена", family_name = missingSurname ? null : "Коваль", verified_email = verified })
                : JsonSerializer.Serialize(new { id = "facebook-subject", email, first_name = "Олена", last_name = missingSurname ? null : "Коваль" });
            return new(HttpStatusCode.OK) { Content = new StringContent(profile) };
        }
    }
    private static IdentityService Identity(ApplicationDbContext db, Provider provider, bool enabled = true)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "external-login-tests-only-key-at-least-32-bytes",
            ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test",
            ["ExternalAuth:StoreOrigin"] = Origin,
            ["ExternalAuth:Google:Enabled"] = enabled.ToString(), ["ExternalAuth:Google:ClientId"] = "google-client", ["ExternalAuth:Google:ClientSecret"] = "google-fixture-secret",
            ["ExternalAuth:Facebook:Enabled"] = enabled.ToString(), ["ExternalAuth:Facebook:ClientId"] = "facebook-client", ["ExternalAuth:Facebook:ClientSecret"] = "facebook-fixture-secret",
        }).Build();
        return new(db, new TokenService(config), new SecondFactorService(db, new Email(), new(Security), Security), config, new HttpClient(provider));
    }
    private static ExternalCodeLoginRequest Request(string provider, string code = "one-time-code") =>
        new(provider, code, Origin + "/api/auth/external/" + provider + "/callback", Verifier);

    [Theory]
    [InlineData("google")]
    [InlineData("facebook")]
    public async Task CodeFlow_RegistersPasswordlessCustomer_ThenLogsIntoSameAccount(string provider)
    {
        await using var db = TelegramTestData.Memory();
        db.Roles.Add(Role.Create(Roles.Customer, Roles.Customer)); await db.SaveChangesAsync();
        var service = Identity(db, new Provider(provider));
        var login = await service.ExchangeExternalCodeAsync(Request(provider));
        Assert.NotNull(login.Authentication); Assert.Null(login.Challenge);
        var user = await db.Users.SingleAsync();
        Assert.Equal("Олена", user.FirstName); Assert.Equal("Коваль", user.LastName);
        Assert.Equal("", user.PasswordHash); Assert.Equal(Roles.Customer, login.Authentication.Role);
        Assert.Equal(provider + "-subject", (await db.UserExternalLogins.SingleAsync()).ProviderUserId);
        var again = await service.ExternalLoginAsync(provider.ToUpperInvariant(), "provider-token");
        Assert.Equal(login.Authentication.UserId, again.Authentication!.UserId);
        Assert.Single(await db.Users.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnlinkExternalProviderAsync(user.Id, provider));
    }

    [Theory]
    [InlineData("google")]
    [InlineData("facebook")]
    public async Task ExistingAccount_RequiresItsConfiguredSecondFactor(string provider)
    {
        await using var db = TelegramTestData.Memory();
        var user = await TelegramTestData.UserAsync(db, Roles.Customer);
        user.EnableEmailTwoFactor(); await db.SaveChangesAsync();
        var service = Identity(db, new Provider(provider, user.Email));
        var login = await service.ExchangeExternalCodeAsync(Request(provider));
        Assert.Null(login.Authentication); Assert.NotNull(login.Challenge);
        Assert.Contains("email", login.Challenge.Methods); Assert.Null(user.RefreshToken);
        db.ChangeTracker.Clear();
        var repeated = await service.ExchangeExternalCodeAsync(Request(provider));
        Assert.Null(repeated.Authentication); Assert.NotNull(repeated.Challenge);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("facebook")]
    public async Task MissingOptionalSurname_DoesNotPreventRegistration(string provider)
    {
        await using var db = TelegramTestData.Memory();
        db.Roles.Add(Role.Create(Roles.Customer, Roles.Customer)); await db.SaveChangesAsync();
        var login = await Identity(db, new Provider(provider, missingSurname: true)).ExchangeExternalCodeAsync(Request(provider));
        Assert.NotNull(login.Authentication); Assert.False(string.IsNullOrWhiteSpace((await db.Users.SingleAsync()).LastName));
    }

    [Fact]
    public async Task PublicConfigurationAndCallback_DoNotExposeSecrets_OrAcceptArbitraryReturnTargets()
    {
        await using var db = TelegramTestData.Memory();
        var service = Identity(db, new Provider("google"));
        Assert.DoesNotContain("fixture-secret", JsonSerializer.Serialize(service.GetExternalProviders()));
        var controller = new AuthController(service, null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = Assert.IsType<RedirectResult>(controller.ExternalCallback("google", "code/+?", "browser-state", null));
        var target = new Uri(result.Url);
        Assert.Equal(Origin + "/shop-api/external/google/callback", target.GetLeftPart(UriPartial.Path));
        Assert.Equal("code/+?", QueryHelpers.ParseQuery(target.Query)["code"]);
        Assert.Equal("browser-state", QueryHelpers.ParseQuery(target.Query)["state"]);
        Assert.IsType<BadRequestResult>(controller.ExternalCallback("untrusted", "code", "state", null));
    }

    [Fact]
    public async Task LinkingUsesTheVerifiedProviderSubject_AndPersistsItForAnExistingUser()
    {
        await using var db = TelegramTestData.Memory();
        var user = await TelegramTestData.UserAsync(db, Roles.Customer);
        var service = Identity(db, new Provider("google"));
        Assert.True(await service.LinkExternalProviderAsync(user.Id, "GOOGLE", "provider-token"));
        db.ChangeTracker.Clear();
        var login = await service.ExternalLoginAsync("google", "provider-token");
        Assert.Equal(user.Id, login.Authentication!.UserId);
        Assert.Equal("google-subject", (await db.UserExternalLogins.SingleAsync()).ProviderUserId);
    }

    [Fact]
    public async Task DisabledProvider_RedirectMismatch_AndInvalidPkceNeverContactProvider()
    {
        await using var db = TelegramTestData.Memory();
        var provider = new Provider("google"); var service = Identity(db, provider);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExchangeExternalCodeAsync(Request("google") with { RedirectUri = "https://untrusted.example/callback" }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExchangeExternalCodeAsync(Request("google") with { CodeVerifier = "short" }));
        var disabled = Identity(db, provider, false);
        Assert.Empty(disabled.GetExternalProviders());
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.ExchangeExternalCodeAsync(Request("google")));
        Assert.Equal(0, provider.Requests);
    }

    [Fact]
    public async Task RejectedCode_UnverifiedEmail_AndBlockedAccountCannotCreateSession()
    {
        await using var db = TelegramTestData.Memory();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Identity(db, new Provider("google")).ExchangeExternalCodeAsync(Request("google", "invalid")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Identity(db, new Provider("google", verified: false)).ExchangeExternalCodeAsync(Request("google")));
        var blocked = await TelegramTestData.UserAsync(db, Roles.Customer); blocked.Block(); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Identity(db, new Provider("facebook", blocked.Email)).ExchangeExternalCodeAsync(Request("facebook")));
        Assert.Null(blocked.RefreshToken); Assert.Empty(await db.UserExternalLogins.ToListAsync());
    }
}
