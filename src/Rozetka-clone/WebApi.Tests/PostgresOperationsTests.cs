using Application.Administration;
using Application.Common;
using Application.Orders;
using Application.Promotions;
using Contracts.Admin.Operations;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace WebApi.Tests;

public sealed class PostgresOperationsTests
{
    private static readonly string Prefix = "treba_qa_ops_" + Guid.NewGuid().ToString("N")[..10] + "_";

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

    private sealed class SynchronizedDiscounts(IDiscountService inner, Func<Task> rendezvous) : IDiscountService
    {
        public async Task<DiscountResult> CalculateDiscountAsync(
            Guid userId,
            IReadOnlyCollection<PriceItem> items,
            string? code,
            CancellationToken ct = default
        )
        {
            var result = await inner.CalculateDiscountAsync(userId, items, code, ct);
            await rendezvous();
            return result;
        }

        public Task<CouponValidationResult> ValidateCouponAsync(
            Guid userId,
            string code,
            CancellationToken ct = default
        ) => inner.ValidateCouponAsync(userId, code, ct);

        public Task RegisterCouponUsageAsync(
            Guid couponId,
            Guid userId,
            Guid orderId,
            CancellationToken ct = default
        ) => inner.RegisterCouponUsageAsync(couponId, userId, orderId, ct);
    }

    [PostgresFact]
    public async Task ConcurrentCheckout_CannotReuseTheLastCoupon_AndLosingOrderDoesNotConsumeStock()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, Model>()
            .Options;
        await using var sql = new NpgsqlConnection(connection);
        await sql.OpenAsync();
        async Task Execute(string text)
        {
            await using var command = new NpgsqlCommand(text, sql);
            await command.ExecuteNonQueryAsync();
        }
        string[] tables = [];
        try
        {
            OperationsTests.Seed seed;
            await using (var db = new ApplicationDbContext(options))
            {
                tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().ToArray();
                Assert.All(tables, t => Assert.StartsWith(Prefix, t));
                await Execute(db.Database.GenerateCreateScript());
                seed = await OperationsTests.SeedAsync(db);
                await new PromotionManagementService(db).SaveAsync(
                    null,
                    new()
                    {
                        Name = "Last coupon",
                        Type = "PERCENTAGE",
                        Status = "ACTIVE",
                        DiscountValue = 10,
                        StartAt = DateTimeOffset.UtcNow.AddHours(-1),
                        EndAt = DateTimeOffset.UtcNow.AddDays(1),
                        Coupons =
                        [
                            new()
                            {
                                Code = "LAST",
                                UsageLimit = 1,
                                UsageLimitPerUser = 1,
                            },
                        ],
                    },
                    default
                );
            }
            var arrived = 0;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Rendezvous()
            {
                if (Interlocked.Increment(ref arrived) == 2)
                    gate.TrySetResult();
                await gate.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }
            async Task<Exception?> Checkout()
            {
                await using var db = new ApplicationDbContext(options);
                var actor = new OperationsTests.Actor(seed.Customer.Id);
                var service = new OrderService(
                    db,
                    new Application.Inventory.InventoryService(db, actor),
                    new SynchronizedDiscounts(new DiscountService(db), Rendezvous),
                    actor
                );
                return await Record.ExceptionAsync(() =>
                    service.CreateOrderAsync(OperationsTests.Request(seed, coupon: "LAST"))
                );
            }
            var errors = await Task.WhenAll(Checkout(), Checkout());
            Assert.Single(errors, e => e is null);
            Assert.IsAssignableFrom<Exception>(Assert.Single(errors, e => e is not null));
            await using var verify = new ApplicationDbContext(options);
            Assert.Equal(1, await verify.Orders.CountAsync());
            Assert.Equal(1, await verify.CouponUsages.CountAsync());
            Assert.Equal(1, (await verify.Coupons.SingleAsync()).UsedCount);
            Assert.Equal(8, (await verify.ProductVariants.SingleAsync(v => v.Id == seed.Variant.Id)).StockQuantity);
            Assert.Equal(1, await verify.InventoryReservations.CountAsync());
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

    [PostgresFact]
    public async Task CheckoutAndReturns_KeepForeignKeysAndTotals_ConcurrentStockWritesConflict_AndFailuresRollback()
    {
        var connection = Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, Model>()
            .Options;
        await using var sql = new NpgsqlConnection(connection);
        await sql.OpenAsync();
        async Task Execute(string statement)
        {
            await using var command = new NpgsqlCommand(statement, sql);
            await command.ExecuteNonQueryAsync();
        }
        string[] tables = [];
        try
        {
            await using (var schema = new ApplicationDbContext(options))
            {
                tables = schema.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().ToArray();
                Assert.All(tables, t => Assert.StartsWith(Prefix, t));
                await Execute(schema.Database.GenerateCreateScript());
            }
            OperationsTests.Seed seed;
            Guid orderId;
            var staff = new OperationsTests.Actor(Guid.NewGuid(), "Manager");
            await using (var db = new ApplicationDbContext(options))
            {
                seed = await OperationsTests.SeedAsync(db);
                var input = new PromotionInput
                {
                    Name = "Postgres coupon",
                    Type = "FIXED_AMOUNT",
                    Status = "ACTIVE",
                    DiscountValue = 30,
                    StartAt = DateTimeOffset.UtcNow.AddHours(-1),
                    EndAt = DateTimeOffset.UtcNow.AddDays(1),
                    Coupons = [new() { Code = "PGOPS", UsageLimit = 5 }],
                };
                await new PromotionManagementService(db).SaveAsync(null, input, default);
                var order = await OperationsTests
                    .Service(db, new(seed.Customer.Id))
                    .CreateOrderAsync(OperationsTests.Request(seed, coupon: "PGOPS"));
                orderId = order.Id;
                Assert.Equal(30, order.DiscountAmount);
                Assert.Equal(220, order.TotalAmount);
                Assert.Equal(1, await db.CouponUsages.CountAsync());
                var service = OperationsTests.Service(db, staff);
                Assert.Single(
                    (await service.ListAsync(1, 20, "AWAITING_PAYMENT", "ORD-", null, null, null, default)).Content
                );
                var metrics = await new OperationsService(db).MetricsAsync(7, default);
                Assert.Equal(1, metrics.NewOrders);
                Assert.Single((await new OperationsService(db).CustomersAsync(1, null, default)).Content);
                Assert.NotNull(await new OperationsService(db).CustomerAsync(seed.Customer.Id, default));
            }
            await using (var stale = new ApplicationDbContext(options))
            await using (var first = new ApplicationDbContext(options))
            {
                var staleStock = await stale.ProductVariants.SingleAsync(v => v.Id == seed.Variant.Id);
                var firstStock = await first.ProductVariants.SingleAsync(v => v.Id == seed.Variant.Id);
                firstStock.SetStock(firstStock.StockQuantity - 1);
                await first.SaveChangesAsync();
                staleStock.SetStock(staleStock.StockQuantity - 1);
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            }
            // A constraint failure after reserving must roll back order, stock and coupon usage together.
            await Execute(
                $"ALTER TABLE \"{Prefix}orders\" ADD CONSTRAINT qa_reject_new_order CHECK (\"Id\" = '{orderId}')"
            );
            await using (var db = new ApplicationDbContext(options))
            {
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    OperationsTests
                        .Service(db, new(seed.Customer.Id))
                        .CreateOrderAsync(OperationsTests.Request(seed, coupon: "PGOPS"))
                );
            }
            await Execute($"ALTER TABLE \"{Prefix}orders\" DROP CONSTRAINT qa_reject_new_order");
            await using (var db = new ApplicationDbContext(options))
            {
                Assert.Equal(7, (await db.ProductVariants.SingleAsync(v => v.Id == seed.Variant.Id)).StockQuantity);
                Assert.Equal(1, await db.Orders.CountAsync());
                Assert.Equal(1, (await db.Coupons.SingleAsync()).UsedCount);
                Assert.Equal(1, await db.CouponUsages.CountAsync());
                var service = OperationsTests.Service(db, staff);
                foreach (
                    var state in new[]
                    {
                        "PAID",
                        "CONFIRMED",
                        "PROCESSING",
                        "READY_FOR_SHIPMENT",
                        "SHIPPED",
                        "DELIVERED",
                    }
                )
                {
                    var details = (await service.DetailsAsync(orderId, default))!;
                    await service.ChangeStatusAsync(
                        orderId,
                        new(state, details.Version, "Postgres validation"),
                        default
                    );
                }
                var support = new SupportService(db, staff, service);
                var ticket = await support.CreateAsync(
                    new()
                    {
                        OrderId = orderId,
                        Kind = "RETURN",
                        Subject = "Return",
                        Message = "All goods returned",
                    },
                    default
                );
                await support.ChangeStatusAsync(ticket.Summary.Id, new("APPROVED", ticket.Summary.Version), default);
                ticket = (await support.DetailsAsync(ticket.Summary.Id, default))!;
                await support.ChangeStatusAsync(ticket.Summary.Id, new("CLOSED", ticket.Summary.Version), default);
                Assert.Equal(9, (await db.ProductVariants.SingleAsync(v => v.Id == seed.Variant.Id)).StockQuantity);
                Assert.Equal(OrderStatus.RETURNED, (await db.Orders.SingleAsync()).Status);
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
