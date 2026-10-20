using System.Buffers.Binary;
using System.Security.Cryptography;
using Application.Abstractions;
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

public sealed class WebsiteLoginTests
{
    private const string Secret = "JBSWY3DPEHPK3PXP";
    private static readonly IOptions<SecurityOptions> Security = Options.Create(new SecurityOptions { EncryptionKey=Convert.ToBase64String(new byte[32]),OtpHashKey=Convert.ToBase64String(new byte[32]) });
    private static TokenService Tokens() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Jwt:SecretKey"]="website-login-tests-only-key-at-least-32-bytes",["Jwt:Issuer"]="test",["Jwt:Audience"]="test" }).Build());
    private sealed class Email : IEmailSender
    { public string? Code; public Task SendSecurityCodeAsync(string recipient,string displayName,string code,TimeSpan validFor,CancellationToken cancellationToken=default){Code=code;return Task.CompletedTask;} }

    [Theory]
    [InlineData("Administrator")][InlineData("Manager")][InlineData("Moderator")][InlineData("Seller")]
    public async Task PasswordLogin_NoBindingsEntersImmediately_ConnectedMethodsOfferOneOfThree(string role)
    {
        await using var db=TelegramTestData.Memory(); var user=await TelegramTestData.UserAsync(db,role);
        user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword("FixtureOnly2026!")); await db.SaveChangesAsync();
        var mail=new Email(); var second=new SecondFactorService(db,mail,new(Security),Security); var identity=new IdentityService(db,Tokens(),second);
        Assert.NotNull((await identity.LoginAsync(new(user.Email,"FixtureOnly2026!"))).Authentication);
        user.BeginAuthenticatorSetup(new AesSecretProtector(Security).Protect(Secret)); user.EnableAuthenticator(); user.EnableEmailTwoFactor();
        await TelegramTestData.BindAsync(db,user,new TelegramTestData.Clock());
        var login=await identity.LoginAsync(new(user.Email,"FixtureOnly2026!"));
        Assert.Null(login.Authentication); Assert.NotNull(login.Challenge); Assert.Equal(new[]{"authenticator","email","telegram"},login.Challenge.Methods); Assert.Null(user.RefreshToken);
        await second.SendLoginEmailCodeAsync(login.Challenge.ChallengeId); Assert.NotNull(mail.Code);
        var verified=await identity.CompleteSecondFactorLoginAsync(new(login.Challenge.ChallengeId,"email",mail.Code)); Assert.Equal(user.Id,verified.UserId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>identity.CompleteSecondFactorLoginAsync(new(login.Challenge.ChallengeId,"authenticator",AuthenticatorCode())));
        var authenticator=await identity.LoginAsync(new(user.Email,"FixtureOnly2026!"));
        Assert.NotNull((await identity.CompleteSecondFactorLoginAsync(new(authenticator.Challenge!.ChallengeId,"authenticator",AuthenticatorCode()))).AccessToken);
    }

    [Fact]
    public async Task GuardAfterPassword_IsBoundToTheSameAccount_AndConsumesTheLoginChallenge()
    {
        await using var db=TelegramTestData.Memory(); var user=await TelegramTestData.UserAsync(db,"Administrator"); var other=await TelegramTestData.UserAsync(db,"Manager");
        var clock=new TelegramTestData.Clock(); await TelegramTestData.BindAsync(db,user,clock); db.TelegramBindings.Add(new(){UserId=other.Id,TelegramUserId=56789,ChatId=56789,LinkedAt=clock.Now}); await db.SaveChangesAsync();
        var identity=TelegramTestData.Identity(db); var second=new SecondFactorService(db,new Email(),new(Security),Security);
        var login=(await second.CreateLoginChallengeAsync(user))!; clock.Now=DateTimeOffset.UtcNow;
        var guard=new TelegramGuardService(db,new(db,TelegramTestData.Options,clock),identity,TelegramTestData.Options,clock);
        var start=await guard.CreateAsync("Website","127.0.0.1",default,login.ChallengeId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>guard.ApproveAsync(start.QrPayload,other.Id,56789,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>identity.CompleteSecondFactorLoginAsync(new(login.ChallengeId,"telegram",start.DisplayCode)));
        await guard.ApproveAsync(start.QrPayload,user.Id,12345,default);
        var result=await guard.CompleteAsync(start.Id,start.BrowserSecret,default); Assert.Equal(user.Id,result.Authentication!.UserId); Assert.Null(result.Challenge);
        Assert.NotNull((await db.AuthenticationChallenges.SingleAsync(c=>c.Id==login.ChallengeId)).ConsumedAt);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>guard.CompleteAsync(start.Id,start.BrowserSecret,default));
    }

    [Fact]
    public async Task AdminEntry_IsShortLivedSingleUse_AndFollowsCurrentRoleAndLoginSession()
    {
        await using var db=TelegramTestData.Memory(); var user=await TelegramTestData.UserAsync(db,"Manager"); user.SetRefreshToken("fixture-session",DateTimeOffset.UtcNow.AddDays(1)); await db.SaveChangesAsync();
        var entry=new AdminEntryService(db,Tokens()); var ticket=await entry.CreateAsync(user.Id,default);
        Assert.InRange(ticket.ExpiresAt-DateTimeOffset.UtcNow,TimeSpan.FromSeconds(50),TimeSpan.FromMinutes(1));
        Assert.DoesNotContain("fixture-session",ticket.Code); Assert.DoesNotContain(ticket.Code.Split('.')[1],(await db.AuthenticationChallenges.SingleAsync()).CodeHash);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>entry.RedeemAsync(ticket.Code[..33]+new string('0',64),default));
        var auth=await entry.RedeemAsync(ticket.Code,default); Assert.Equal(user.Id,auth.UserId); Assert.Equal("fixture-session",auth.RefreshToken);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>entry.RedeemAsync(ticket.Code,default));
        var stale=await entry.CreateAsync(user.Id,default); user.SetRefreshToken("new-session",DateTimeOffset.UtcNow.AddDays(1));await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>entry.RedeemAsync(stale.Code,default));
        var customer=await TelegramTestData.UserAsync(db,"Customer");customer.SetRefreshToken("customer-session",DateTimeOffset.UtcNow.AddDays(1));await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>entry.CreateAsync(customer.Id,default));
        var expired=await entry.CreateAsync(user.Id,default);var expiredId=Guid.ParseExact(expired.Code[..32],"N");var row=await db.AuthenticationChallenges.SingleAsync(c=>c.Id==expiredId); db.Entry(row).Property(c=>c.ExpiresAt).CurrentValue=DateTimeOffset.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>entry.RedeemAsync(expired.Code,default));
        var revoked=await entry.CreateAsync(user.Id,default);user.Block();await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>entry.RedeemAsync(revoked.Code,default));
    }

    private static string AuthenticatorCode()
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var bytes=new List<byte>();var buffer=0;var bits=0;
        foreach(var c in Secret){buffer=(buffer<<5)|alphabet.IndexOf(c);bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(buffer>>bits));}}
        Span<byte> counter=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(counter,DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30);
        var hash=HMACSHA1.HashData(bytes.ToArray(),counter);var offset=hash[^1]&15;var value=BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset,4))&0x7fffffff;return (value%1000000).ToString("D6");
    }

    private static readonly string Prefix="treba_qa_site_"+Guid.NewGuid().ToString("N")[..10]+"_";
    public sealed class SiteModel(ModelCustomizerDependencies dependencies):ModelCustomizer(dependencies)
    {public override void Customize(ModelBuilder builder,DbContext context){base.Customize(builder,context);foreach(var entity in builder.Model.GetEntityTypes())if(entity.GetTableName() is {} table){entity.SetTableName(Prefix+table);entity.SetSchema("public");}}}
    [PostgresFact]
    public async Task PostgreSql_AdminEntry_CannotBeRedeemedByTwoConcurrentRequests()
    {
        var connection=Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer,SiteModel>().Options;
        await using var sql=new NpgsqlConnection(connection);await sql.OpenAsync();string[] tables=[];
        async Task Execute(string statement){await using var command=new NpgsqlCommand(statement,sql);await command.ExecuteNonQueryAsync();}
        try{
            await using var first=new ApplicationDbContext(options); tables=first.Model.GetEntityTypes().Select(e=>e.GetTableName()!).Distinct().ToArray(); await PostgresTestTables.CreateAsync(first,sql,Prefix);
            var user=await TelegramTestData.UserAsync(first,"Manager");user.SetRefreshToken("concurrent-session",DateTimeOffset.UtcNow.AddDays(1));await first.SaveChangesAsync();
            var entry=await new AdminEntryService(first,Tokens()).CreateAsync(user.Id,default);
            await using var second=new ApplicationDbContext(options);
            // Both requests read the same unconsumed challenge before either saves.
            var id=Guid.ParseExact(entry.Code[..32],"N");await second.AuthenticationChallenges.SingleAsync(c=>c.Id==id);
            await new AdminEntryService(first,Tokens()).RedeemAsync(entry.Code,default);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>new AdminEntryService(second,Tokens()).RedeemAsync(entry.Code,default));
        }finally{foreach(var table in tables.Reverse()){Assert.StartsWith(Prefix,table);await Execute($"DROP TABLE IF EXISTS public.\"{table}\" CASCADE");}}
    }
}
