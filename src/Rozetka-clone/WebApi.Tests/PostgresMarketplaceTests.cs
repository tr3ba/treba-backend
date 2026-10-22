using Application.Marketplace;
using Contracts.Marketplace;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace WebApi.Tests;

public sealed class PostgresMarketplaceTests
{
    private static readonly string Prefix = "treba_qa_market_" + Guid.NewGuid().ToString("N")[..10] + "_";
    public sealed class Model(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            foreach (var entity in builder.Model.GetEntityTypes()) if (entity.GetTableName() is { } table) { entity.SetTableName(Prefix + table); entity.SetSchema("public"); }
        }
    }

    [PostgresFact]
    public async Task MarketplaceRows_PersistWithForeignKeys_RejectStaleWrites_AndUseUniqueNotificationReceipts()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer, Model>().Options;
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        async Task Execute(string command) { await using var cmd = new NpgsqlCommand(command, sql); await cmd.ExecuteNonQueryAsync(); }
        string[] tables = [];
        try
        {
            OperationsTests.Seed seed; SellerApplicationView application;
            await using (var db = new ApplicationDbContext(options))
            {
                tables = db.Model.GetEntityTypes().Select(x => x.GetTableName()!).Distinct().ToArray(); Assert.All(tables, t => Assert.StartsWith(Prefix, t));
                await Execute(db.Database.GenerateCreateScript()); seed = await OperationsTests.SeedAsync(db);
                var attribute = new Domain.Entities.Attribute.Attribute(Guid.NewGuid(), seed.Product.CategoryId, "Memory", "memory", Domain.Enums.AttributeType.NUMBER, false, true, false, "GB", 0);
                db.Attributes.Add(attribute);
                db.ProductAttributeValues.Add(new() { Id = Guid.NewGuid(), ProductId = seed.Product.Id, AttributeId = attribute.Id, NumberValue = 16 });
                await db.SaveChangesAsync();
                var catalog = new Application.Catalog.CatalogService(db);
                Assert.Single((await catalog.ListAsync(1, 24, null, null, true, seed.Product.CategoryId, "priceasc", default, filters: new() { Attributes = new() { [attribute.Id] = "16" } })).Content);
                Assert.Empty((await catalog.ListAsync(1, 24, null, null, true, null, null, default, filters: new() { Attributes = new() { [attribute.Id] = "unmatched" } })).Content);
                Assert.Equal("16", Assert.Single(Assert.Single((await catalog.PublicFacetsAsync(seed.Product.CategoryId, null, default)).Attributes).Options).Value);
                var actor = new OperationsTests.Actor(seed.Customer.Id);
                application = await new SellerApplicationService(db, actor).ApplyAsync(MarketplaceWorkflowTests.Input(), default);
                var account = new CustomerAccountService(db, actor); var before = await account.PreferencesAsync(default);
                await account.SavePreferencesAsync(new(new() { ["addressCity"] = "Persisted city" }, before.Version), default);
            }
            await using (var db = new ApplicationDbContext(options))
            {
                Assert.Equal("Persisted city", (await new CustomerAccountService(db, new OperationsTests.Actor(seed.Customer.Id)).PreferencesAsync(default)).Values["addressCity"]);
                var manager = new SellerApplicationService(db, new OperationsTests.Actor(seed.Seller.User.Id, "Manager"));
                var approved = await manager.ReviewAsync(application.Id, new(application.Version, true, "Approved", "pg-fixture-shop"), default);
                Assert.Equal("approved", approved.Status);
            }
            await using (var db = new ApplicationDbContext(options))
            {
                var seller = await db.Sellers.SingleAsync(x => x.UserId == seed.Customer.Id); Assert.Single(await db.Stores.Where(x => x.SellerId == seller.Id).ToListAsync());
                var actor = new OperationsTests.Actor(seed.Customer.Id, "Seller"); var notifications = new NotificationService(db, actor);
                var item = Assert.Single((await notifications.FeedAsync(default)).Items, x => x.Kind == "application");
                await notifications.ReadAsync([item.Key], default); await notifications.ReadAsync([item.Key], default);
                Assert.Equal(1, await db.NotificationReceipts.CountAsync());
            }
            await using (var first = new ApplicationDbContext(options))
            await using (var second = new ApplicationDbContext(options))
            {
                var a = await first.CustomerPreferences.SingleAsync(); var b = await second.CustomerPreferences.SingleAsync();
                a.Version = Guid.NewGuid(); await first.SaveChangesAsync(); b.Version = Guid.NewGuid();
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            }
        }
        finally { foreach (var table in tables.Reverse()) { Assert.StartsWith(Prefix, table); await Execute($"DROP TABLE IF EXISTS public.\"{table}\" CASCADE"); } }
    }
}
