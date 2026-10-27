using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebApi.Administration;

namespace WebApi.Tests;

public sealed class DeploymentMigrationTests
{
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
            foreach (
                var migration in db.Database.GetMigrations().TakeWhile(m => m != AdminOperationsDeployment.Migration)
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
            // Expect NormalizeProfileLanguages and any subsequent allowed migrations (e.g., AddUserExternalLogin)
            Assert.Contains(AdminOperationsDeployment.Migration, pending);
            AdminOperationsDeployment.ValidatePendingMigrations(pending);
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
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
