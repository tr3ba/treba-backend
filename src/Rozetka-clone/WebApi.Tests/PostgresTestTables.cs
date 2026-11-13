using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace WebApi.Tests;

internal static class PostgresTestTables
{
    // Build only the explicitly prefixed tables from this model. No shared schema is needed.
    // These focused query/concurrency tests intentionally omit foreign keys, as their previous
    // CREATE TABLE LIKE fixtures did; ORM relationships and unique indexes remain intact.
    internal static async Task CreateAsync(DbContext db, NpgsqlConnection connection, string prefix)
    {
        Assert.StartsWith("treba_qa_", prefix);
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel())
            .Where(operation =>
                operation is CreateTableOperation table && table.Name.StartsWith(prefix, StringComparison.Ordinal)
                || operation is CreateIndexOperation index && index.Table.StartsWith(prefix, StringComparison.Ordinal)
            )
            .ToArray();

        foreach (var index in operations.OfType<CreateIndexOperation>())
        {
            var originalName = index.Name;
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"{prefix}:{index.Table}:{originalName}")
                )
            )[..16];

            index.Name = $"qa_{hash}";
        }
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        Assert.NotEmpty(tables);
        foreach (var table in tables)
            table.ForeignKeys.Clear();
        foreach (var statement in db.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
        {
            await using var command = new NpgsqlCommand(statement.CommandText, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    internal static void RenameIndexes(ModelBuilder builder, string prefix)
    {
        Assert.StartsWith("treba_qa_", prefix);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var index in entity.GetIndexes())
            {
                var originalName = index.GetDatabaseName();
                var tableName = entity.GetTableName();

                var hash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(
                            $"{prefix}:{tableName}:{originalName}"
                        )
                    )
                )[..16];

                index.SetDatabaseName($"qa_{hash}");
            }
        }
    }
}
