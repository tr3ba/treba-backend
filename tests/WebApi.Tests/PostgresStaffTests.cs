using Application.Abstractions;
using Contracts.Admin.Settings;
using Domain.Entities.Users;
using Infrastructure.Administration;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace WebApi.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")))
            Skip = "Set TREBA_TEST_POSTGRES to a development PostgreSQL connection with CREATE permission.";
    }
}

public sealed class PostgresStaffTests
{
    private static readonly string Prefix = "treba_qa_" + Guid.NewGuid().ToString("N")[..12] + "_";
    private sealed record Actor(Guid? UserId) : ICurrentActor
    {
        public string Name => "PostgreSQL Test Administrator";
        public string Role => "Administrator";
    }

    public sealed class QaModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(entity => entity.ClrType == typeof(User) || entity.ClrType == typeof(Role) || entity.ClrType == typeof(AdminAuditLog)))
            {
                entity.SetTableName(Prefix + entity.GetTableName());
                entity.SetSchema("public");
            }
        }
    }

    [PostgresFact]
    public async Task ConcurrentAdministratorBlocks_KeepOneAdministrator_AndAuditCommitsAtomically()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer, QaModelCustomizer>().Options;
        var names = new[] { Prefix + "admin_audit_logs", Prefix + "users", Prefix + "roles" };
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        async Task Execute(string statement) { await using var command = new NpgsqlCommand(statement, sql); await command.ExecuteNonQueryAsync(); }
        try
        {
            // Copies only schema; production application rows are never copied or changed.
            foreach (var table in new[] { "roles", "users", "admin_audit_logs" })
                await Execute($"CREATE TABLE public.\"{Prefix}{table}\" (LIKE public.\"{table}\" INCLUDING ALL)");
            Guid firstId, secondId;
            await using (var db = new ApplicationDbContext(options))
            {
                var role = Role.Create("Administrator", "QA administrator");
                db.Roles.AddRange(role, Role.Create("Manager", "QA manager"));
                var first = User.Create(Guid.NewGuid(), "first@qa.invalid", null, "First", "QA"); first.AssignRole(role.Id); first.VerifyEmail();
                var second = User.Create(Guid.NewGuid(), "second@qa.invalid", null, "Second", "QA"); second.AssignRole(role.Id); second.VerifyEmail();
                firstId = first.Id; secondId = second.Id;
                db.Users.AddRange(first, second); await db.SaveChangesAsync();
            }
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loaded = 0;
            async Task<bool> Block(Guid actor, Guid target)
            {
                await using var db = new ApplicationDbContext(options, new Actor(actor));
                var user = await db.Users.SingleAsync(user => user.Id == target); user.Block();
                if (Interlocked.Increment(ref loaded) == 2) ready.SetResult();
                await ready.Task;
                try { await db.SaveChangesAsync(); return true; }
                catch (UserDomainException) { return false; }
            }
            var results = await Task.WhenAll(Block(firstId, secondId), Block(secondId, firstId)).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Single(results, success => success);
            Guid surviving;
            await using (var db = new ApplicationDbContext(options))
            {
                var active = await db.Users.Where(user => user.Status == UserStatus.Active).ToListAsync(); Assert.Single(active); surviving = active[0].Id;
                Assert.Single(await db.AdminAuditLogs.ToListAsync());
            }
            await Execute($"ALTER TABLE public.\"{Prefix}admin_audit_logs\" ADD CONSTRAINT qa_reject_audit CHECK (\"EntityName\" <> 'fail@qa.invalid')");
            await using (var db = new ApplicationDbContext(options, new Actor(surviving)))
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => new StaffSettingsService(db).CreateAsync(new CreateStaffRequest
                { Email = "fail@qa.invalid", FirstName = "Failure", LastName = "QA", Role = "Manager", Password = "QA-private-password-123!" }, default));
            }
            await using (var db = new ApplicationDbContext(options))
            {
                Assert.False(await db.Users.AnyAsync(user => user.Email == "fail@qa.invalid"));
                Assert.Single(await db.AdminAuditLogs.ToListAsync());
            }
        }
        finally
        {
            // Remove only this invocation's tables owned by the current database user.
            await using var owned = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tableowner=current_user AND tablename=ANY(@names)", sql);
            owned.Parameters.AddWithValue("names", names);
            var tables = new HashSet<string>();
            await using (var reader = await owned.ExecuteReaderAsync()) { while (await reader.ReadAsync()) tables.Add(reader.GetString(0)); }
            foreach (var name in names.Where(tables.Contains)) await Execute($"DROP TABLE public.\"{name}\"");
        }
    }
}
