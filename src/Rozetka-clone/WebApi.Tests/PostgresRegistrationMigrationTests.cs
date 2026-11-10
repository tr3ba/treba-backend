using Domain.Entities.Users;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace WebApi.Tests;

public sealed class PostgresRegistrationMigrationTests
{
    [PostgresFact]
    public async Task RegistrationMigration_PreservesExistingChallenges_AddsDeliveryAddressAndIndex_AndIsIdempotent()
    {
        const string target = "20261029143639_AddRegistrationVerification";
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var schema = "treba_qa_registration_" + Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(connection);
        await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", owner))
            await create.ExecuteNonQueryAsync();
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema + ",pg_catalog" }.ConnectionString;
            await using var sql = new NpgsqlConnection(isolated);
            await sql.OpenAsync();
            await using (var baseline = new NpgsqlCommand("""
                CREATE TABLE authentication_challenges (
                    "Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "Purpose" varchar(50) NOT NULL,
                    "CodeHash" varchar(128), "ExpiresAt" timestamptz NOT NULL, "CreatedAt" timestamptz NOT NULL,
                    "LastCodeSentAt" timestamptz, "ConsumedAt" timestamptz, "FailedAttempts" integer NOT NULL);
                INSERT INTO authentication_challenges VALUES (
                    '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222',
                    'Login', 'preserved-code-hash', now() + interval '1 hour', now(), now(), NULL, 2);
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
                """, sql))
                await baseline.ExecuteNonQueryAsync();

            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(isolated).Options);
            var migrations = db.Database.GetMigrations().ToArray();
            Assert.Contains(target, migrations);
            foreach (var migration in migrations.TakeWhile(migration => migration != target))
            {
                await using var record = new NpgsqlCommand("INSERT INTO \"__EFMigrationsHistory\" VALUES (@id, '10.0.11')", sql);
                record.Parameters.AddWithValue("id", migration);
                await record.ExecuteNonQueryAsync();
            }
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(target);
            await migrator.MigrateAsync(target);
            Assert.Equal(target, (await db.Database.GetAppliedMigrationsAsync()).Last());

            var existing = await db.AuthenticationChallenges.AsNoTracking()
                .Select(c => new { c.UserId, c.Purpose, c.CodeHash, c.FailedAttempts, c.ConsumedAt, c.DeliveryAddress }).SingleAsync();
            Assert.Equal(AuthenticationChallengePurpose.Login, existing.Purpose);
            Assert.Equal("preserved-code-hash", existing.CodeHash);
            Assert.Equal(2, existing.FailedAttempts);
            Assert.Null(existing.ConsumedAt);
            Assert.Null(existing.DeliveryAddress);

            await using (var index = new NpgsqlCommand("""
                SELECT indexdef FROM pg_indexes WHERE schemaname = @schema
                    AND indexname = 'IX_authentication_challenges_DeliveryAddress_LastCodeSentAt'
                """, sql))
            {
                index.Parameters.AddWithValue("schema", schema);
                var definition = Assert.IsType<string>(await index.ExecuteScalarAsync());
                Assert.Contains("\"DeliveryAddress\", \"LastCodeSentAt\"", definition);
            }

            var registrationId = Guid.NewGuid();
            await using var insertRegistration = new NpgsqlCommand("""
                INSERT INTO authentication_challenges ("Id", "UserId", "Purpose", "CodeHash", "ExpiresAt", "CreatedAt",
                    "LastCodeSentAt", "ConsumedAt", "FailedAttempts", "DeliveryAddress")
                VALUES (@id, @user, 'RegistrationSms', 'fixture-registration-hash', now() + interval '5 minutes', now(),
                    now(), NULL, 0, '380671234567')
                """, sql);
            insertRegistration.Parameters.AddWithValue("id", registrationId);
            insertRegistration.Parameters.AddWithValue("user", existing.UserId);
            await insertRegistration.ExecuteNonQueryAsync();
            Assert.Equal("380671234567", await db.AuthenticationChallenges.Where(c => c.Id == registrationId)
                .Select(c => c.DeliveryAddress).SingleAsync());
        }
        finally
        {
            Assert.StartsWith("treba_qa_registration_", schema);
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", owner);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
