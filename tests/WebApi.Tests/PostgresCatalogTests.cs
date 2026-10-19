using Application.Catalog;
using Application.Products;
using Contracts.Catalog;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace WebApi.Tests;

public sealed class PostgresCatalogTests
{
    private static readonly string Prefix = "treba_qa_cat_" + Guid.NewGuid().ToString("N")[..12] + "_";
    private static readonly string[] Tables = ["categories", "brands", "sellers", "stores", "products", "product_variants", "product_images", "attributes", "attribute_options", "product_attribute_values", "admin_audit_logs"];
    public sealed class QaModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            foreach (var entity in builder.Model.GetEntityTypes())
                if (entity.GetTableName() is { } name && Tables.Contains(name)) { entity.SetTableName(Prefix + name); entity.SetSchema("public"); }
        }
    }

    [PostgresFact]
    public async Task PostgreSql_FiltersTranslate_ConcurrentEditsConflict_AndModerationAuditFailureRollsBackStatus()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer, QaModelCustomizer>().Options;
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        async Task Execute(string text) { await using var cmd = new NpgsqlCommand(text, sql); await cmd.ExecuteNonQueryAsync(); }
        try
        {
            // Schema copies have their own indexes and no foreign keys to shared application tables.
            foreach (var name in Tables) await Execute($"CREATE TABLE public.\"{Prefix}{name}\" (LIKE public.\"{name}\" INCLUDING ALL)");
            CatalogTestData.Seed seed; CatalogProductDto product;
            await using (var db = new ApplicationDbContext(options))
            {
                seed = await CatalogTestData.SeedAsync(db); var catalog = new CatalogService(db);
                product = (await catalog.SaveAsync(null, seed.Store.Id, CatalogTestData.Request(seed), default))!;
                var result = await catalog.ListAsync(1, 12, "seller alpha", "DRAFT", false, seed.Root.Id, "stockasc", default, seed.Seller.UserId,
                    new() { SellerId = seed.Seller.Id, BrandId = seed.Brand.Id, MinPrice = 150, MaxPrice = 220, Stock = "low" });
                Assert.Equal(product.Id, Assert.Single(result.Content).Id);
                Assert.Equal(2, (await catalog.GetAsync(product.Id, default))!.Variants.Count);
                Assert.Equal(2, (await catalog.FilterOptionsAsync(seed.Seller.UserId, default)).Categories.Count);
            }
            var request = CatalogTestData.Request(seed);
            request.Variants = product.Variants.ToList(); request.ExpectedUpdatedAt = product.UpdatedAt; request.Name = "Changed by first editor"; request.Variants[0].Price = 110;
            await using (var first = new ApplicationDbContext(options))
            await using (var second = new ApplicationDbContext(options))
            {
                await second.Products.SingleAsync(p => p.Id == product.Id);
                await new CatalogService(first).SaveAsync(product.Id, Guid.Empty, request, default);
                request.Name = "Stale second editor"; request.Variants[0].Price = 999; request.Variants[0].OldPrice = null;
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new CatalogService(second).SaveAsync(product.Id, Guid.Empty, request, default));
            }
            await using (var db = new ApplicationDbContext(options))
            {
                var current = (await new CatalogService(db).GetAsync(product.Id, default))!;
                Assert.Equal("Changed by first editor", current.Name); Assert.Equal(110, current.Variants.Single(v => v.Id == product.Variants[0].Id).Price);
            }
            await Execute($"ALTER TABLE public.\"{Prefix}admin_audit_logs\" ADD CONSTRAINT qa_reject_moderation CHECK (\"Action\" <> 'Moderation')");
            await using (var db = new ApplicationDbContext(options))
                await Assert.ThrowsAsync<DbUpdateException>(() => new ProductService(db).SubmitForModerationAsync(product.Id));
            await using (var db = new ApplicationDbContext(options))
            {
                Assert.Equal(ProductStatus.DRAFT, (await db.Products.SingleAsync()).Status);
                Assert.Empty(await db.AdminAuditLogs.ToListAsync());
            }
        }
        finally
        {
            var names = Tables.Select(t => Prefix + t).ToArray();
            await using var owned = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tableowner=current_user AND tablename=ANY(@names)", sql);
            owned.Parameters.AddWithValue("names", names); var cleanup = new HashSet<string>();
            await using (var reader = await owned.ExecuteReaderAsync()) { while (await reader.ReadAsync()) cleanup.Add(reader.GetString(0)); }
            foreach (var name in names.Reverse().Where(cleanup.Contains)) await Execute($"DROP TABLE public.\"{name}\"");
        }
    }
}
