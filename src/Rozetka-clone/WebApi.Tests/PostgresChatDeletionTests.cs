using Application.Administration;
using Contracts.Admin.Operations;
using Domain.Entities;
using Domain.Entities.Support;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace WebApi.Tests;

public sealed class PostgresChatDeletionTests
{
    private static readonly string Prefix = "treba_qa_trash_" + Guid.NewGuid().ToString("N")[..10] + "_";

    public sealed class Model(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            foreach (var entity in builder.Model.GetEntityTypes())
                if (entity.GetTableName() is { } table)
                {
                    entity.SetTableName(Prefix + table);
                    entity.SetSchema("public");
                }
        }
    }

    [PostgresFact]
    public async Task Migration_PreservesExistingThreads_AndReplacesActiveChatIndexes()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var schema = Prefix + "migration";
        Assert.StartsWith("treba_qa_trash_", schema);
        await using var owner = new NpgsqlConnection(connection);
        await owner.OpenAsync();
        async Task OwnerCommand(string text)
        {
            await using var command = new NpgsqlCommand(text, owner);
            await command.ExecuteNonQueryAsync();
        }
        await OwnerCommand($"CREATE SCHEMA \"{schema}\"");
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection)
            {
                SearchPath = schema + ",pg_catalog",
            }.ConnectionString;
            await using var sql = new NpgsqlConnection(isolated);
            await sql.OpenAsync();
            async Task Execute(string text)
            {
                await using var command = new NpgsqlCommand(text, sql);
                await command.ExecuteNonQueryAsync();
            }
            async Task<long> Count(string text)
            {
                await using var command = new NpgsqlCommand(text, sql);
                return (long)(await command.ExecuteScalarAsync())!;
            }
            await Execute(
                """
                CREATE TABLE support_tickets ("Id" uuid PRIMARY KEY, "CustomerId" uuid NOT NULL, "ProductId" uuid, "OrderId" uuid,
                    "Kind" varchar(20) NOT NULL, "Status" varchar(20) NOT NULL, "Subject" text NOT NULL);
                CREATE UNIQUE INDEX "IX_support_tickets_CustomerId" ON support_tickets ("CustomerId") WHERE "Kind"='CHAT' AND "Status"<>'CLOSED';
                CREATE UNIQUE INDEX "IX_support_tickets_CustomerId_ProductId" ON support_tickets ("CustomerId", "ProductId") WHERE "Kind"='QUESTION' AND "ProductId" IS NOT NULL AND "Status"<>'CLOSED';
                CREATE UNIQUE INDEX "IX_support_tickets_OrderId_Kind" ON support_tickets ("OrderId", "Kind") WHERE "Kind"='RETURN' AND "Status" IN ('OPEN','IN_PROGRESS','APPROVED');
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
                """
            );
            var user = Guid.NewGuid();
            var old = Guid.NewGuid();
            await Execute(
                $"INSERT INTO support_tickets (\"Id\",\"CustomerId\",\"Kind\",\"Status\",\"Subject\") VALUES ('{old}','{user}','CHAT','OPEN','Preserved history')"
            );
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(isolated).Options
            );
            var script = db.GetService<IMigrator>()
                .GenerateScript(
                    "20261022084526_AddMarketplaceWorkflows",
                    "20261022132542_AddAdminFeaturesAndChatTrash",
                    MigrationsSqlGenerationOptions.NoTransactions
                );
            await Execute(script);
            Assert.Equal(
                1,
                await Count(
                    "SELECT count(*) FROM support_tickets WHERE \"Subject\"='Preserved history' AND NOT \"IsDeleted\""
                )
            );
            Assert.Equal(0, await Count("SELECT count(*) FROM feature_flags"));
            await Execute($"UPDATE support_tickets SET \"IsDeleted\"=TRUE WHERE \"Id\"='{old}'");
            await Execute(
                $"INSERT INTO support_tickets (\"Id\",\"CustomerId\",\"Kind\",\"Status\",\"Subject\") VALUES ('{Guid.NewGuid()}','{user}','CHAT','OPEN','New conversation')"
            );
            Assert.Equal(2, await Count("SELECT count(*) FROM support_tickets"));
        }
        finally
        {
            Assert.StartsWith("treba_qa_trash_", schema);
            await OwnerCommand($"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    [PostgresFact]
    public async Task Trash_PreservesRelationalHistory_AllowsAReplacementChat_AndRejectsAnInflightMessageAfterDeletion()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, Model>()
            .Options;
        await using var sql = new NpgsqlConnection(connection);
        await sql.OpenAsync();
        async Task Execute(string command)
        {
            await using var cmd = new NpgsqlCommand(command, sql);
            await cmd.ExecuteNonQueryAsync();
        }
        string[] tables = [];
        try
        {
            OperationsTests.Seed seed;
            TicketDetails chat;
            await using (var db = new ApplicationDbContext(options))
            {
                tables = db.Model.GetEntityTypes().Select(x => x.GetTableName()!).Distinct().ToArray();
                Assert.All(tables, t => Assert.StartsWith(Prefix, t));
                await Execute(db.Database.GenerateCreateScript());
                seed = await OperationsTests.SeedAsync(db);
                db.FeatureFlags.Add(new() { Key = FeatureFlag.ChatDeletion, Enabled = true });
                await db.SaveChangesAsync();
                chat = await FeatureChatDeletionTests.Chat(FeatureChatDeletionTests.Support(db, new(seed.Customer.Id)));
            }
            await using (var first = new ApplicationDbContext(options))
            await using (var second = new ApplicationDbContext(options))
            {
                // Simulate a message request which already read the old version before an administrator deletes the thread.
                var inflight = await second.SupportTickets.SingleAsync();
                inflight.Version = Guid.NewGuid();
                inflight.UpdatedAt = DateTimeOffset.UtcNow;
                var messageId = Guid.NewGuid();
                second.SupportMessages.Add(
                    new SupportMessage
                    {
                        Id = messageId,
                        TicketId = chat.Summary.Id,
                        AuthorId = seed.Customer.Id,
                        AuthorName = "Fixture",
                        AuthorRole = "Customer",
                        Message = "Concurrent message",
                    }
                );
                var manager = FeatureChatDeletionTests.Support(first, new(seed.Seller.User.Id, "Manager"));
                await manager.DeleteAsync(chat.Summary.Id, new(chat.Summary.Version), default);
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
                Assert.False(await first.SupportMessages.AnyAsync(m => m.Id == messageId));
            }
            await using (var db = new ApplicationDbContext(options))
            {
                var customer = FeatureChatDeletionTests.Support(db, new(seed.Customer.Id));
                var replacement = await FeatureChatDeletionTests.Chat(customer);
                Assert.NotEqual(chat.Summary.Id, replacement.Summary.Id);
                Assert.Single(await db.SupportTickets.ToListAsync());
                Assert.Equal(2, await db.SupportTickets.IgnoreQueryFilters().CountAsync());
                Assert.Equal(2, await db.SupportMessages.CountAsync());
                var manager = FeatureChatDeletionTests.Support(db, new(seed.Seller.User.Id, "Manager"));
                await manager.ChangeStatusAsync(
                    replacement.Summary.Id,
                    new("CLOSED", replacement.Summary.Version),
                    default
                );
                var trash = Assert.Single((await manager.DeletedAsync(1, null, default)).Content);
                await manager.RestoreAsync(trash.Id, new(trash.Version), default);
                Assert.NotNull(await customer.DetailsAsync(chat.Summary.Id, default));
            }
            await using (var first = new ApplicationDbContext(options))
            await using (var second = new ApplicationDbContext(options))
            {
                var a = await first.FeatureFlags.SingleAsync();
                var b = await second.FeatureFlags.SingleAsync();
                a.Enabled = false;
                a.Version = Guid.NewGuid();
                await first.SaveChangesAsync();
                b.Version = Guid.NewGuid();
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            }
        }
        finally
        {
            foreach (var table in tables.Reverse())
            {
                Assert.StartsWith(Prefix, table);
                await Execute($"DROP TABLE IF EXISTS public.\"{table}\" CASCADE");
            }
        }
    }
}
