using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WebApi.Administration;

namespace WebApi.Tests;

public sealed class DeploymentMigrationTests
{
    [PostgresFact]
    public async Task DeploymentPrivilegeLookup_ResolvesExternalLoginTableWithItsActualCase()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var schema = "treba_qa_external_" + Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(connection);
        await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"; CREATE TABLE \"{schema}\".\"UserExternalLogins\" (\"Id\" uuid PRIMARY KEY)", owner))
            await create.ExecuteNonQueryAsync();
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema + ",pg_catalog" }.ConnectionString;
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(isolated).Options);
            var relation = AdminOperationsDeployment.ExternalLoginTableIdentifier(db);
            // PostgreSQL must resolve the model's quoted name; the former snake_case lookup misses this table.
            Assert.True(await db.Database.SqlQuery<bool>($"SELECT has_table_privilege(to_regclass({relation}), 'SELECT') AS \"Value\"").SingleAsync());
            Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('user_external_logins') IS NULL AS \"Value\"").SingleAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", owner);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void DeploymentAcceptsMigrationsFromOperationsBaseline_AndRejectsUnreviewedUpdates()
    {
        using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused").Options
        );
        var migrations = db
            .Database.GetMigrations()
            .SkipWhile(m => m != "20261019213326_AddReviewOrderItemRelation")
            .ToArray();
        Assert.NotEmpty(migrations);
        Assert.Contains(AdminOperationsDeployment.Migration, migrations);
        AdminOperationsDeployment.ValidatePendingMigrations(migrations);
        AdminOperationsDeployment.ValidatePendingMigrations([]);
        Assert.Throws<InvalidOperationException>(() =>
            AdminOperationsDeployment.ValidatePendingMigrations([
                AdminOperationsDeployment.Migration,
                "20990101000000_UnreviewedSchemaChange",
            ])
        );
    }

    [PostgresFact]
    public async Task DeploymentUpgrade_NormalizesExistingLanguages_AndIsIdempotent()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var schema = "treba_qa_deploy_" + Guid.NewGuid().ToString("N");
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
            await using (
                var command = new NpgsqlCommand(
                    """
                    CREATE TABLE user_profiles ("Id" integer PRIMARY KEY, "Language" text NOT NULL, "DisplayName" text NOT NULL);
                    CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
                    """,
                    sql
                )
            )
                await command.ExecuteNonQueryAsync();
            string[] before = ["uk", "en", "ru", "ru-RU", "RU", " en-GB ", "en-us", "de", ""];
            for (var i = 0; i < before.Length; i++)
            {
                await using var insert = new NpgsqlCommand(
                    "INSERT INTO user_profiles VALUES (@id, @language, 'Preserved profile')",
                    sql
                );
                insert.Parameters.AddWithValue("id", i);
                insert.Parameters.AddWithValue("language", before[i]);
                await insert.ExecuteNonQueryAsync();
            }
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(isolated).Options
            );
            const string normalizeLanguagesMigration = "20261023110000_NormalizeProfileLanguages";
            foreach (
                var migration in db.Database.GetMigrations().TakeWhile(m => m != normalizeLanguagesMigration)
            )
            {
                await using var insert = new NpgsqlCommand(
                    "INSERT INTO \"__EFMigrationsHistory\" VALUES (@migration, '10.0.11')",
                    sql
                );
                insert.Parameters.AddWithValue("migration", migration);
                await insert.ExecuteNonQueryAsync();
            }
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
            // NormalizeProfileLanguages must be among pending migrations
            Assert.Contains(normalizeLanguagesMigration, pending);
            // All pending must be in the allowed list (validates no surprise migrations)
            AdminOperationsDeployment.ValidatePendingMigrations(pending);
            // Apply only the NormalizeProfileLanguages migration (test target).
            // Other allowed migrations may require tables that don't exist in this minimal test schema.
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(normalizeLanguagesMigration);
            await migrator.MigrateAsync(normalizeLanguagesMigration); // idempotency
            // NormalizeProfileLanguages should now be applied
            var remaining = (await db.Database.GetPendingMigrationsAsync()).ToArray();
            Assert.DoesNotContain(normalizeLanguagesMigration, remaining);
            await using var check = new NpgsqlCommand(
                "SELECT \"Language\", \"DisplayName\" FROM user_profiles ORDER BY \"Id\"",
                sql
            );
            await using var reader = await check.ExecuteReaderAsync();
            string[] expected = ["uk", "en", "uk", "uk", "uk", "en", "en", "uk", "uk"];
            foreach (var language in expected)
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(language, reader.GetString(0));
                Assert.Equal("Preserved profile", reader.GetString(1));
            }
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            Assert.StartsWith("treba_qa_deploy_", schema);
            await OwnerCommand($"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }
}
