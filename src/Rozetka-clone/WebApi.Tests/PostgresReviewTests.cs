using Application.Marketplace;
using Application.Reviews;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace WebApi.Tests;

public sealed class PostgresReviewTests
{
    private static readonly string Prefix = "treba_qa_review_" + Guid.NewGuid().ToString("N")[..10] + "_";
    public sealed class Model(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            foreach (var entity in builder.Model.GetEntityTypes())
                if (entity.GetTableName() is { } table) { entity.SetTableName(Prefix + table); entity.SetSchema("public"); }
        }
    }

    private sealed class ConcurrentReviewSave : SaveChangesInterceptor
    {
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Review>().Any(e => e.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    [PostgresFact]
    public async Task ReceivedPurchase_EligibilityQueriesTranslate_AndConcurrentSubmissionsPersistOnlyOneReview()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer, Model>().Options;
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        await using var db = new ApplicationDbContext(options);
        var tables = db.Model.GetEntityTypes().Select(x => x.GetTableName()!).Distinct().ToArray();
        Assert.All(tables, table => Assert.StartsWith(Prefix, table));
        try
        {
            await using (var create = new NpgsqlCommand(db.Database.GenerateCreateScript(), sql)) await create.ExecuteNonQueryAsync();
            var seed = await OperationsTests.SeedAsync(db);
            var buyer = new OperationsTests.Actor(seed.Customer.Id);
            var created = await OperationsTests.Service(db, buyer).CreateOrderAsync(OperationsTests.Request(seed));
            var staff = OperationsTests.Service(db, new(seed.Seller.User.Id, "Manager"));
            foreach (var status in new[] { "PAID", "CONFIRMED", "PROCESSING", "READY_FOR_SHIPMENT", "SHIPPED", "DELIVERED" })
                await staff.ChangeStatusAsync(created.Id, new(status, (await staff.DetailsAsync(created.Id, default))!.Version, null), default);
            var item = Assert.Single((await new ReviewService(db).GetProductsAwaitingReviewAsync(seed.Customer.Id, 1)).Items);
            var interceptor = new ConcurrentReviewSave();
            var concurrentOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection)
                .ReplaceService<IModelCustomizer, Model>().AddInterceptors(interceptor).Options;
            async Task<bool> Submit()
            {
                await using var competing = new ApplicationDbContext(concurrentOptions);
                try
                {
                    await new ReviewService(competing).CreateReviewAsync(seed.Customer.Id,
                        new() { OrderItemId = item.OrderItemId, ProductId = item.ProductId, Rating = 5, Comment = "Отримано" });
                    return true;
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { return false; }
            }
            var outcomes = await Task.WhenAll(Submit(), Submit());
            Assert.Single(outcomes, success => success);
            Assert.Single(await db.Reviews.ToListAsync());
            Assert.Empty((await new ReviewService(db).GetProductsAwaitingReviewAsync(seed.Customer.Id, 1)).Items);
            var reviewedItem = Assert.Single((await new CustomerAccountService(db, buyer).OrderAsync(created.Id, default))!.Items);
            Assert.False(reviewedItem.CanReview); Assert.NotNull(reviewedItem.ReviewId);
            Assert.Single((await new ReviewService(db).GetMyReviewsAsync(seed.Customer.Id, 1)).Items);
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
}
