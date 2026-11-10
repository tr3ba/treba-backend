using Application.Abstractions;
using Application.Catalog;
using Contracts.Authentication;
using Domain.Entities.Users;
using Infrastructure.Authentication;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class PostgresStandaloneSecurityTests
{
    private static readonly string Prefix = "treba_qa_security_" + Guid.NewGuid().ToString("N")[..10] + "_";
    public sealed class Model(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            foreach (var entity in builder.Model.GetEntityTypes())
                if (entity.GetTableName() is { } table)
                { entity.SetTableName(Prefix + table); entity.SetSchema("public"); }
        }
    }
    private sealed class Mail : IEmailSender
    {
        public int Sends;
        public string? Code;
        public Task SendSecurityCodeAsync(string recipient, string displayName, string code, TimeSpan validFor, CancellationToken ct = default)
        { Interlocked.Increment(ref Sends); Code = code; return Task.CompletedTask; }
    }
    private sealed class Sms : ISmsSender
    {
        public bool IsConfigured => false;
        public Task SendAsync(string phone, string message, Guid id, CancellationToken ct = default) => throw new InvalidOperationException();
    }

    [PostgresFact]
    public async Task PostgreSql_RegistrationSendsMfaBudgetsAndQueueRetries_AreSerializedAcrossContexts()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!)
            .ReplaceService<IModelCustomizer, Model>().Options;
        await using var setup = new ApplicationDbContext(options);
        var tables = setup.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().ToArray();
        Assert.All(tables, t => Assert.StartsWith(Prefix, t));
        await using (var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand(setup.Database.GenerateCreateScript(), connection);
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var security = Options.Create(new SecurityOptions
            {
                EncryptionKey = Convert.ToBase64String(new byte[32]),
                OtpHashKey = Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray())
            });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "standalone-test-fixture-key-at-least-32-bytes", ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test"
            }).Build();
            var mail = new Mail();
            var smtp = Options.Create(new SmtpEmailOptions { Enabled = true, Host = "fixture", FromAddress = "fixture@example.test" });
            RegistrationService Registration(ApplicationDbContext db) => new(db, new TokenService(config), mail, new Sms(), security, smtp);
            setup.Roles.Add(Role.Create(Roles.Customer, Roles.Customer));
            await setup.SaveChangesAsync();
            var request = new RegisterRequest("parallel@example.test", "Password123!", "Test", "Owner", null, VerificationMethod: "email");
            async Task<RegistrationChallengeResponse> Register()
            { await using var db = new ApplicationDbContext(options); return await Registration(db).BeginAsync(request); }
            await Task.WhenAll(Register(), Register());
            Assert.Equal(1, mail.Sends);
            Assert.Equal(1, await setup.AuthenticationChallenges.CountAsync());
            var pending = await setup.AuthenticationChallenges.SingleAsync();
            setup.Entry(pending).Property(c => c.LastCodeSentAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-2);
            await setup.SaveChangesAsync();
            var owner = await Registration(setup).BeginAsync(request with { Password = "OwnerPassword456!" });
            await Registration(setup).VerifyAsync(new(owner.ChallengeId, mail.Code!));
            var user = await setup.Users.SingleAsync();
            Assert.True(BCrypt.Net.BCrypt.Verify("OwnerPassword456!", user.PasswordHash));
            Assert.False(BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash));
            user.EnableEmailTwoFactor();
            await setup.SaveChangesAsync();
            var factors = new SecondFactorService(setup, mail, new(security), security);
            var ids = new List<Guid>();
            for (var i = 0; i < 6; i++) ids.Add((await factors.CreateLoginChallengeAsync(user))!.ChallengeId);
            async Task Reject(Guid id)
            {
                await using var db = new ApplicationDbContext(options);
                var service = new SecondFactorService(db, mail, new(security), security);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VerifyLoginChallengeAsync(new(id, "email", "wrong")));
            }
            await Task.WhenAll(ids.Select(Reject));
            await setup.Entry(user).ReloadAsync();
            Assert.Equal(5, user.MfaFailedAttempts);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => factors.CreateLoginChallengeAsync(user));

            const string secret = "JBSWY3DPEHPK3PXP";
            user.DisableEmailTwoFactor();
            user.BeginAuthenticatorSetup(new AesSecretProtector(security).Protect(secret));
            user.EnableAuthenticator();
            setup.Entry(user).Property(u => u.MfaWindowStart).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-16);
            await setup.SaveChangesAsync();
            var firstTotp = (await factors.CreateLoginChallengeAsync(user))!.ChallengeId;
            var secondTotp = (await factors.CreateLoginChallengeAsync(user))!.ChallengeId;
            var generator = typeof(SecondFactorService).Assembly.GetType("Infrastructure.Authentication.TotpUtility")!
                .GetMethod("GenerateCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var code = (string)generator.Invoke(null, [secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30])!;
            async Task<bool> VerifyTotp(Guid id)
            {
                await using var db = new ApplicationDbContext(options);
                try
                {
                    await new SecondFactorService(db, mail, new(security), security).VerifyLoginChallengeAsync(new(id, "authenticator", code));
                    return true;
                }
                catch (UnauthorizedAccessException) { return false; }
            }
            Assert.Single(await Task.WhenAll(VerifyTotp(firstTotp), VerifyTotp(secondTotp)), accepted => accepted);

            var seed = await TelegramTestData.SeedAsync(setup);
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 49; i++) setup.SellerImportJobs.Add(new() { UserId = seed.User.Id, SourceUrl = $"https://shop.example.test/{i}", CreatedAt = now, UpdatedAt = now });
            var failed = new Domain.Entities.Telegram.SellerImportJob { UserId = seed.User.Id, SourceUrl = "https://shop.example.test/failed", Status = "Failed" };
            setup.SellerImportJobs.Add(failed);
            await setup.SaveChangesAsync();
            async Task<bool> Queue(bool retry)
            {
                await using var db = new ApplicationDbContext(options);
                var queue = new SellerImportQueue(db, new SellerCategoryAccess(db), new TelegramTestData.Clock());
                try
                {
                    if (retry) await queue.RetryAsync(seed.User.Id, failed.Id, default);
                    else await queue.AddAsync(seed.User.Id, new([new("https://shop.example.test/new")]), default);
                    return true;
                }
                catch (Application.Common.BusinessRuleException) { return false; }
            }
            Assert.Single(await Task.WhenAll(Queue(false), Queue(true)), success => success);
            Assert.Equal(50, await setup.SellerImportJobs.CountAsync(j => j.Status == "Queued"));

            var other = await TelegramTestData.UserAsync(setup, Roles.Seller);
            setup.Sellers.Add(new Domain.Entities.Seller
            {
                Id = Guid.NewGuid(), UserId = other.Id, CompanyName = "Other seller",
                TaxNumber = Guid.NewGuid().ToString("N"), Status = Domain.Enums.SellerStatus.ACTIVE
            });
            var otherJob = new Domain.Entities.Telegram.SellerImportJob
            {
                UserId = other.Id, SourceUrl = "https://shop.example.test/other", Status = "Queued",
                CreatedAt = now.AddSeconds(1), UpdatedAt = now.AddSeconds(1)
            };
            setup.SellerImportJobs.Add(otherJob);
            await setup.SaveChangesAsync();
            var clock = new TelegramTestData.Clock { Now = now.AddSeconds(2) };
            async Task Work()
            {
                await using var db = new ApplicationDbContext(options);
                var runner = new SellerWorkspaceRunner(db,
                    TelegramTestData.Import(db, clock, new TelegramTestData.Source(), new TelegramTestData.Media()),
                    null!, TelegramTestData.Options, clock);
                Assert.True(await runner.ImportOneAsync(default));
            }
            await Work();
            await Work();
            Assert.Equal(1, (await setup.SellerImportJobs.AsNoTracking().SingleAsync(j => j.Id == otherJob.Id)).Attempts);
            Assert.True(await setup.SellerImportJobs.CountAsync(j => j.UserId == seed.User.Id && j.Status == "Queued") >= 48);
        }
        finally
        {
            foreach (var table in tables)
            {
                await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"DROP TABLE IF EXISTS \"{table}\" CASCADE", connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
