using Application.Marketplace;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using WebApi.Administration;
using WebApi.Notifications;

namespace WebApi.Tests;

public sealed class PostgresDeliveryNotificationTests
{
    private static readonly string Prefix = "treba_qa_delivery_" + Guid.NewGuid().ToString("N")[..10] + "_";
    public sealed class Model(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            foreach (var entity in builder.Model.GetEntityTypes())
                if (entity.GetTableName() is { } table) { entity.SetTableName(Prefix + table); entity.SetSchema("public"); }
        }
    }

    private sealed class BlockingMail : Application.Abstractions.ITransactionalEmailSender
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Sends;
        public async Task SendNotificationAsync(string recipient, string title, string text, string url, CancellationToken ct)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); Sends++; }
    }

    [PostgresFact]
    public async Task QuoteConflict_RollsBackOrderStockAndOutbox_AndNotificationLeasePreventsTwoSends()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer, Model>().Options;
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        string[] tables = [];
        try
        {
            OperationsTests.Seed seed;
            var clock = new TelegramTestData.Clock();
            await using var first = new ApplicationDbContext(options);
            tables = first.Model.GetEntityTypes().Select(x => x.GetTableName()!).Distinct().ToArray();
            Assert.All(tables, t => Assert.StartsWith(Prefix, t));
            await using (var create = new NpgsqlCommand(first.Database.GenerateCreateScript(), sql)) await create.ExecuteNonQueryAsync();
            seed = await OperationsTests.SeedAsync(first);
            first.CustomerNotificationSettings.Add(new() { UserId = seed.Customer.Id, OrdersEmail = true }); await first.SaveChangesAsync();
            var actor = new OperationsTests.Actor(seed.Customer.Id);
            var delivery = DeliveryAndNotificationTests.Delivery(first, actor, clock);
            var items = new List<Contracts.Delivery.DeliveryQuoteItem> { new(seed.Variant.Id, 2) };
            var quote = await delivery.QuoteAsync(new(DeliveryAndNotificationTests.CityId, DeliveryAndNotificationTests.PointId, items), default);
            await using var loser = new ApplicationDbContext(options);
            var otherDelivery = DeliveryAndNotificationTests.Delivery(loser, actor, clock);
            await otherDelivery.ValidateAsync(quote.Id, seed.Customer.Id, items, null, 200, 200, default);
            var request = OperationsTests.Request(seed) with { DeliveryQuoteId = quote.Id };
            await DeliveryAndNotificationTests.Orders(first, actor, delivery).CreateOrderAsync(request);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => DeliveryAndNotificationTests.Orders(loser, actor, otherDelivery).CreateOrderAsync(request));
            await using var verify = new ApplicationDbContext(options);
            Assert.Single(await verify.Orders.ToListAsync()); Assert.Single(await verify.CustomerNotifications.ToListAsync());
            Assert.Equal(8, (await verify.ProductVariants.SingleAsync()).StockQuantity);
            Assert.Single(await verify.OrderShipments.ToListAsync());
            var shipment = await verify.OrderShipments.SingleAsync();
            var account = new CustomerAccountService(verify, actor, DeliveryAndNotificationTests.Delivery(verify, actor, clock));
            Assert.Null(shipment.Reference);
            Assert.Empty((await account.ShipmentsAsync(1, null, default)).Content);
            clock.Now = clock.Now.AddMinutes(1);
            var mail = new BlockingMail();
            var config = Options.Create(new CustomerNotificationOptions { Enabled = true, StoreOrigin = "https://treba.example" });
            await using var worker1 = new ApplicationDbContext(options); await using var worker2 = new ApplicationDbContext(options);
            var running = new CustomerNotificationRunner(worker1, mail, new DeliveryAndNotificationTests.Bot(), new DeliveryAndNotificationTests.Channels(), config, clock).DeliverOneAsync(default);
            await mail.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(await new CustomerNotificationRunner(worker2, mail, new DeliveryAndNotificationTests.Bot(), new DeliveryAndNotificationTests.Channels(), config, clock).DeliverOneAsync(default));
            mail.Release.TrySetResult(); Assert.True(await running); Assert.Equal(1, mail.Sends);
            var manager = new OperationsTests.Actor(seed.Seller.User.Id, "Manager");
            var fulfillment = DeliveryAndNotificationTests.Orders(verify, manager, DeliveryAndNotificationTests.Delivery(verify, manager, clock));
            var orderId = (await verify.Orders.SingleAsync()).Id;
            var version = (await fulfillment.DetailsAsync(orderId, default))!.Version;
            await using var concurrent = new ApplicationDbContext(options);
            var competing = DeliveryAndNotificationTests.Orders(concurrent, manager, DeliveryAndNotificationTests.Delivery(concurrent, manager, clock));
            await competing.DetailsAsync(orderId, default);
            await fulfillment.ChangeStatusAsync(orderId, new("PAID", version, "Received"), default);
            await Assert.ThrowsAsync<Application.Common.BusinessRuleException>(() => competing.ChangeStatusAsync(orderId, new("PAID", version, "Received"), default));
            Assert.NotNull(shipment.Reference);
            Assert.Single((await account.ShipmentsAsync(1, shipment.Reference, default)).Content);
        }
        finally
        {
            foreach (var table in tables)
            {
                Assert.StartsWith(Prefix, table);
                await using var drop = new NpgsqlCommand($"DROP TABLE IF EXISTS \"{table}\" CASCADE", sql); await drop.ExecuteNonQueryAsync();
            }
        }
    }

    [Fact]
    public void DeliveryMigration_MatchesCurrentModel()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task DeliveryMigration_PreservesExistingData_AndReapplicationDoesNothing()
    {
        const string deliveryMigration = "20261024192735_AddDeliveryAndCustomerNotifications";
        const string shipmentMigration = "20261026142406_AddManagedShipmentLifecycle";
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var schema = "treba_qa_delivery_migration_" + Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(connection); await owner.OpenAsync();
        async Task Execute(string text) { await using var command = new NpgsqlCommand(text, owner); await command.ExecuteNonQueryAsync(); }
        await Execute($"CREATE SCHEMA \"{schema}\"");
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema + ",pg_catalog" }.ConnectionString;
            await using var sql = new NpgsqlConnection(isolated); await sql.OpenAsync();
            await using (var baseline = new NpgsqlCommand("""
                CREATE TABLE users ("Id" uuid PRIMARY KEY);
                CREATE TABLE orders ("Id" uuid PRIMARY KEY, "Note" text NOT NULL, "Status" text NOT NULL DEFAULT 'AWAITING_PAYMENT');
                INSERT INTO orders ("Id", "Note") VALUES ('33333333-3333-3333-3333-333333333333', 'Preserved order');
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
                """, sql)) await baseline.ExecuteNonQueryAsync();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(isolated).Options);
            foreach (var migration in db.Database.GetMigrations().TakeWhile(m => m != deliveryMigration))
            {
                await using var record = new NpgsqlCommand("INSERT INTO \"__EFMigrationsHistory\" VALUES (@id, '10.0.11')", sql);
                record.Parameters.AddWithValue("id", migration); await record.ExecuteNonQueryAsync();
            }
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(deliveryMigration);
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO orders ("Id", "Note", "Status") VALUES ('44444444-4444-4444-4444-444444444444', 'Paid order', 'PAID');
                INSERT INTO order_shipments ("OrderId", "CityRef", "PickupPointRef", "City", "PickupPoint", "Postomat", "Reference", "QuotedPrice", "WeightKg", "EstimatedWeight")
                VALUES ('33333333-3333-3333-3333-333333333333', '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 'Київ', 'Відділення №1', false, 'TREBA-DEMO-UNPAID', 90, 1, false),
                       ('44444444-4444-4444-4444-444444444444', '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 'Київ', 'Відділення №2', false, 'TREBA-DEMO-PAID', 90, 1, false);
                """, sql)) await seed.ExecuteNonQueryAsync();
            // This fixture contains only the delivery baseline. Later authentication or catalog
            // migrations must not run against its intentionally incomplete schema.
            await migrator.MigrateAsync(shipmentMigration);
            await migrator.MigrateAsync(shipmentMigration);
            Assert.Equal(shipmentMigration, (await db.Database.GetAppliedMigrationsAsync()).Last());
            Assert.All(await db.Database.GetPendingMigrationsAsync(), migration =>
                Assert.True(StringComparer.Ordinal.Compare(migration, shipmentMigration) > 0));
            Assert.Empty(await db.CustomerNotifications.ToListAsync()); Assert.Empty(await db.DeliveryQuotes.ToListAsync());
            var shipments = await db.OrderShipments.AsNoTracking().OrderBy(s => s.OrderId).ToListAsync();
            Assert.Null(shipments[0].Reference); Assert.Equal("TRB-PAID", shipments[1].Reference);
            Assert.All(shipments, s => Assert.Equal("NovaPoshta", s.Carrier));
            Assert.Equal("Відділення №2", shipments[1].PickupPoint);
            await using var check = new NpgsqlCommand("SELECT \"Note\" FROM orders WHERE \"Id\" = '33333333-3333-3333-3333-333333333333'", sql); Assert.Equal("Preserved order", await check.ExecuteScalarAsync());
        }
        finally { Assert.StartsWith("treba_qa_", schema); await Execute($"DROP SCHEMA \"{schema}\" CASCADE"); }
    }
}
