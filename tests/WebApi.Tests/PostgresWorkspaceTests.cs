using System.Text.Json;
using Application.Catalog;
using Application.Products;
using Contracts.Telegram;
using Domain.Entities.Users;
using Infrastructure.Administration;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using WebApi.Telegram;

namespace WebApi.Tests;

public sealed class PostgresWorkspaceTests
{
    private static readonly string Prefix = "treba_qa_ws_" + Guid.NewGuid().ToString("N")[..10] + "_";
    public sealed class Model(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            foreach (var entity in builder.Model.GetEntityTypes())
                if (entity.GetTableName() is { } table) { entity.SetTableName(Prefix + table); entity.SetSchema("public"); }
        }
    }

    [PostgresFact]
    public async Task PostgreSql_WorkspaceProvisioning_ConcurrentQueue_BulkConflict_Duplicates_AndOutbox()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer, Model>().Options;
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        async Task Execute(string text) { await using var command = new NpgsqlCommand(text, sql); await command.ExecuteNonQueryAsync(); }
        string[] tables = [];
        try
        {
            await using (var schema = new ApplicationDbContext(options))
            {
                tables = schema.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().ToArray();
                Assert.All(tables, t => Assert.StartsWith(Prefix, t)); await Execute(schema.Database.GenerateCreateScript());
            }
            var clock = new TelegramTestData.Clock(); TelegramTestData.Seed seed; Guid productId;
            await using (var db = new ApplicationDbContext(options))
            {
                seed = await TelegramTestData.SeedAsync(db); await TelegramTestData.AllowImportAsync(db, seed);
                var created = await new SellerProvisioningService(db).CreateAsync(new() { Email = "new@qa.invalid", Password = "Test-password-123!", FirstName = "QA", LastName = "Seller", CompanyName = "QA seller", StoreName = "QA store", StoreSlug = "qa-workspace", TestSeller = true, AllCategories = true }, default);
                Assert.Equal(2, await db.SellerCategoryGrants.CountAsync(g => g.SellerId == created.SellerId));
                var product = await new CatalogService(db).SaveAsync(null, seed.Store.Id, new() { Name = "QA product", Slug = "qa-product", StoreId = seed.Store.Id, CategoryId = seed.Category.Id, BrandId = seed.Brand.Id, ManufacturerPartNumber = "QA-123", Variants = [new() { Name = "Main", Sku = "QA-SKU", Barcode = "4820000000001", Price = 100, OldPrice = 120, StockQuantity = 5 }] }, default);
                productId = product!.Id;
                Assert.Single(await new CatalogDuplicateService(db).FindAsync(seed.User.Id, seed.Brand.Id, "qa 123", null, null, default));
            }
            async Task<Guid> Queue()
            {
                await using var db = new ApplicationDbContext(options);
                return Assert.Single(await new SellerImportQueue(db, new(db), clock).AddAsync(seed.User.Id, new([new(TelegramTestData.SourceUrl)]), default)).Id;
            }
            var queued = await Task.WhenAll(Queue(), Queue()); Assert.Equal(queued[0], queued[1]);
            var protection = new EphemeralDataProtectionProvider();
            await using (var stale = new ApplicationDbContext(options))
            await using (var first = new ApplicationDbContext(options))
            {
                var bulk = new BulkCatalogService(first, protection, new(first), clock);
                var preview = await bulk.PreviewAsync(seed.User.Id, seed.User.Id, new() { ProductIds = [productId], Price = 150, StockQuantity = 8 }, default);
                Assert.Null(Assert.Single(preview.Rows).AfterOldPrice);
                await stale.Products.SingleAsync(p => p.Id == productId); await stale.ProductVariants.SingleAsync(v => v.ProductId == productId);
                Assert.Equal(1, await bulk.ApplyAsync(seed.User.Id, seed.User.Id, preview.Token, default));
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new BulkCatalogService(stale, protection, new(stale), clock).ApplyAsync(seed.User.Id, seed.User.Id, preview.Token, default));
            }
            var bot = new Bot();
            await using (var db = new ApplicationDbContext(options))
            {
                Assert.Null((await db.ProductVariants.SingleAsync(v => v.ProductId == productId)).OldPrice);
                var moderator = await TelegramTestData.UserAsync(db, Roles.Moderator); await TelegramTestData.BindAsync(db, moderator, clock, 77777);
                await new ProductService(db).SubmitForModerationAsync(productId); Assert.Single(await db.TelegramNotifications.ToListAsync());
                clock.Now = DateTimeOffset.UtcNow.AddMinutes(1);
                var runner = new SellerWorkspaceRunner(db, TelegramTestData.Import(db, clock, new TelegramTestData.Source(), new TelegramTestData.Media()), bot, TelegramTestData.Options, clock);
                Assert.True(await runner.DeliverOneAsync(default)); Assert.Single(bot.Messages);
                Assert.True(await runner.ImportOneAsync(default));
                Assert.Equal("Ready", Assert.Single(await new SellerImportQueue(db, new(db), clock).ListAsync(seed.User.Id, default)).Status);
                Assert.Single(await db.Products.ToListAsync()); // Queued imports create previews only.
            }
        }
        finally
        {
            await using var owned = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tableowner=current_user AND tablename=ANY(@names)", sql);
            owned.Parameters.AddWithValue("names", tables); var names = new List<string>();
            await using (var reader = await owned.ExecuteReaderAsync()) { while (await reader.ReadAsync()) names.Add(reader.GetString(0)); }
            if (names.Count > 0) { Assert.All(names, n => Assert.StartsWith(Prefix, n)); await Execute("DROP TABLE " + string.Join(',', names.Select(n => "public.\"" + n.Replace("\"", "\"\"") + "\"")) + " CASCADE"); }
        }
    }
    private sealed class Bot : ITelegramBotClient
    {
        internal List<string> Messages = [];
        public Task<JsonDocument> CallAsync(string method, object payload, CancellationToken ct) { Messages.Add(JsonSerializer.Serialize(payload)); return Task.FromResult(JsonDocument.Parse("{\"ok\":true,\"result\":{}}")); }
        public Task SendMenuAsync(long chatId, string text, CancellationToken ct) => throw new NotSupportedException();
        public Task SendTextAsync(long chatId, string text, CancellationToken ct) => throw new NotSupportedException();
    }
}
