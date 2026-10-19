using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Application.Catalog;
using Application.Products;
using Contracts.Telegram;
using Domain.Entities;
using Domain.Entities.Product;
using Domain.Entities.Telegram;
using Domain.Entities.Users;
using Domain.Enums;
using Infrastructure.Authentication;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WebApi.Telegram;

namespace WebApi.Tests;

internal static class TelegramTestData
{
    internal const string BotToken = "123456789:TEST_FIXTURE_ONLY_ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    internal static readonly IOptions<TelegramOptions> Options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions {
        Enabled = true, BotToken = BotToken, BotUsername = "TrebaTestBot", MiniAppUrl = "https://treba.example/telegram/" });
    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal static ApplicationDbContext Memory() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    internal static async Task<User> UserAsync(ApplicationDbContext db, string roleName = Roles.Seller)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == roleName) ?? Role.Create(roleName, roleName);
        if (db.Entry(role).State == EntityState.Detached) db.Roles.Add(role);
        var user = User.Create(Guid.NewGuid(), Guid.NewGuid().ToString("N") + "@example.test", null, "Test", roleName);
        user.AssignRole(role.Id); user.VerifyEmail(); db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }
    internal sealed record Seed(User User, Category Root, Category Category, Brand Brand, Seller Seller, Store Store);
    internal static async Task<Seed> SeedAsync(ApplicationDbContext db)
    {
        var user = await UserAsync(db); var now = DateTime.UtcNow;
        var root = new Category { Id = Guid.NewGuid(), Name = "Electronics", Slug = "electronics", IsActive = true };
        var category = new Category { Id = Guid.NewGuid(), ParentId = root.Id, Name = "Phones", Slug = "phones", IsActive = true, Level = 1 };
        var brand = new Brand { Id = Guid.NewGuid(), Name = "Test Brand", Slug = "test-brand", IsActive = true };
        var seller = new Seller { Id = Guid.NewGuid(), UserId = user.Id, CompanyName = "Test Seller", TaxNumber = "TEST", Status = SellerStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
        var store = new Store { Id = Guid.NewGuid(), SellerId = seller.Id, Name = "Test Store", Slug = "test-store", IsActive = true, CreatedAt = now, UpdatedAt = now };
        db.AddRange(root, category, brand, seller, store); await db.SaveChangesAsync(); return new(user, root, category, brand, seller, store);
    }
    internal static IdentityService Identity(ApplicationDbContext db)
    {
        var security = Microsoft.Extensions.Options.Options.Create(new SecurityOptions {
            EncryptionKey = Convert.ToBase64String(new byte[32]), OtpHashKey = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:SecretKey"] = "treba-tests-only-jwt-key-at-least-thirty-two-bytes", ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test" }).Build();
        return new(db, new TokenService(configuration), new SecondFactorService(db, new NoEmail(), new AesSecretProtector(security), security));
    }
    private sealed class NoEmail : IEmailSender
    {
        public Task SendSecurityCodeAsync(string recipient, string displayName, string code, TimeSpan validFor, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Guard must not send email automatically.");
    }
    internal static string Signed(long id, DateTimeOffset date, string? token = null, bool isBot = false)
    {
        // Independent protocol fixture: fields are sorted before signing and escaped only afterwards.
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal) {
            ["auth_date"] = date.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["query_id"] = "test-query", ["user"] = JsonSerializer.Serialize(new { id, first_name = "Test", last_name = "User", is_bot = isBot }) };
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token ?? BotToken));
        fields["hash"] = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', fields.Select(f => f.Key + "=" + f.Value))))).ToLowerInvariant();
        return string.Join('&', fields.Select(f => Uri.EscapeDataString(f.Key) + "=" + Uri.EscapeDataString(f.Value)));
    }
    internal static async Task BindAsync(ApplicationDbContext db, User user, Clock clock, long telegramId = 12345)
    {
        var links = new TelegramLinkService(db, Options, clock); var start = await links.BeginAsync(user.Id, default);
        await links.ClaimAsync(start.BotUrl.Split("link_")[1], telegramId, telegramId, "test_user", "Telegram User", default);
        await links.ConfirmAsync(user.Id, start.Id, default);
    }
    internal const string SourceUrl = "https://rozetka.com.ua/test-phone/p123456/";
    internal const string Html = """
        <html><head><script type="application/ld+json">
        {"@graph":[{"@type":"BreadcrumbList","itemListElement":[
          {"name":"Electronics","item":"https://rozetka.com.ua/electronics/c80000/"},
          {"name":"Phones","item":{"@id":"https://rozetka.com.ua/phones/c80001/"}}]},
         {"@type":"Product","sku":"123456","name":"Test <b>Phone</b>",
          "description":"<p>Good phone</p><script>bad()</script>","brand":{"name":"Test Brand"},
          "image":["https://content.rozetka.com.ua/goods/images/big/123.png","https://evil.example/secret.png"],
          "offers":{"price":199.90,"priceCurrency":"UAH"},
          "additionalProperty":[{"name":"Memory","value":"128 GB"}]}]}</script></head><body></body></html>
        """;
    internal sealed class Source : IMarketplaceSourceClient
    {
        internal int Downloads;
        public Task<string> GetPageAsync(Uri uri, CancellationToken ct) => Task.FromResult(Html.Replace("<script>bad()</script>", "<script>bad()<\\/script>", StringComparison.Ordinal));
        public Task<byte[]> GetImageAsync(Uri uri, CancellationToken ct) { Downloads++; return Task.FromResult(new byte[] { 137,80,78,71,13,10,26,10,0 }); }
    }
    internal sealed class Media : IProductMediaStorage
    {
        internal bool Fail;
        internal HashSet<string> Files = [];
        public Task SaveAsync(Guid productId, string fileName, byte[] bytes, CancellationToken ct) { Files.Add(fileName); if (Fail) throw new IOException("Test storage unavailable"); return Task.CompletedTask; }
        public Task<ProductMediaFile?> ReadAsync(Guid productId, string fileName, CancellationToken ct) => Task.FromResult<ProductMediaFile?>(null);
        public Task DeleteAsync(Guid productId, string fileName, CancellationToken ct) { Files.Remove(fileName); return Task.CompletedTask; }
    }
    internal static ProductImportService Import(ApplicationDbContext db, Clock clock, Source source, Media media) => new(db, source, new(), new SellerCategoryAccess(db), new CatalogService(db), new ProductService(db), media, clock);
    internal static SubmitImportRequest Submit(Seed seed) => new() { Name = "My Test Phone", Description = "Reviewed description", StoreId = seed.Store.Id, BrandId = seed.Brand.Id, Sku = "IMPORT-TEST-123", Price = 250, StockQuantity = 3 };
    internal static async Task AllowImportAsync(ApplicationDbContext db, Seed seed)
    {
        db.AddRange(new SellerCategoryGrant { SellerId = seed.Seller.Id, CategoryId = seed.Root.Id, IncludeDescendants = true },
            new RozetkaCategoryMapping { ExternalKey = "c80001", CategoryId = seed.Category.Id }); await db.SaveChangesAsync();
    }
}
