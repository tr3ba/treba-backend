using Infrastructure.Persistence;
using Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Administration;

// Operator-only commands: never start HTTP, workers, Telegram polling or notifications.
public static class AdminOperationsDeployment
{
    public static string ExternalLoginTableIdentifier(ApplicationDbContext db) =>
        "\"" + db.Model.FindEntityType(typeof(UserExternalLogin))!.GetTableName()!.Replace("\"", "\"\"") + "\"";

    public const string Migration = "20261030094652_AddReviewRepliesAndTemplates";
    private static readonly HashSet<string> AllowedPending =
        ["20261017112413_AddPromotions", "20261019213326_AddReviewOrderItemRelation", "20261020093201_AddAdminOperations", "20261020104554_AddSupportConversations", "20261020144055_AddWebsiteAdminLogin", "20261021191511_AddDelivery", "20261022084526_AddMarketplaceWorkflows", "20261022132542_AddAdminFeaturesAndChatTrash", "20261023110000_NormalizeProfileLanguages", "20261024192735_AddDeliveryAndCustomerNotifications", "20261025000601_AddUserExternalLogin", "20261026142406_AddManagedShipmentLifecycle", "20261029143639_AddRegistrationVerification", Migration];

    public static void ValidatePendingMigrations(IEnumerable<string> migrations)
    {
        var pending = migrations.ToArray();
        if (pending.Any(p => !AllowedPending.Contains(p)))
            throw new InvalidOperationException("Unexpected pending migrations require a separate reviewed database update: " + string.Join(", ", pending));
    }

    public static async Task<int> RunAsync(IServiceProvider services, bool migrate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync(timeout.Token)).ToArray();
            ValidatePendingMigrations(pending);
            if (pending.Length > 0)
            {
                Console.WriteLine("Pending operational migrations: " + string.Join(", ", pending));
                if (!migrate) throw new InvalidOperationException("Apply the operational database migration before deploying this API.");
                // Fail before DDL when the runtime account cannot own the schema update.
                var canMigrate = await db.Database.SqlQueryRaw<bool>("""
                    SELECT has_schema_privilege('public', 'CREATE') AND
                        (SELECT bool_and(pg_has_role(c.relowner, 'USAGE') OR r.rolsuper)
                         FROM pg_class c CROSS JOIN pg_roles r
                         WHERE r.rolname = current_user AND c.oid IN
                            (to_regclass('public.orders'), to_regclass('public.promotions'),
                             to_regclass('public.reviews'), to_regclass('public.users'), to_regclass('public.products'), to_regclass('public.support_tickets'))) AS "Value"
                    """).SingleAsync(timeout.Token);
                if (!canMigrate) throw new InvalidOperationException("The API database account cannot apply DDL. Apply the reviewed operational SQL scripts with the database owner, then retry deployment.");
                await db.Database.MigrateAsync(timeout.Token);
                Console.WriteLine("Operational database migrations applied.");
            }
            var applied = await db.Database.GetAppliedMigrationsAsync(timeout.Token);
            if (!applied.Contains(Migration)) throw new InvalidOperationException("The operational database migration is missing.");
            var externalLoginTable = "public." + ExternalLoginTableIdentifier(db);
            var privileges = await db.Database.SqlQuery<bool>($"""
                SELECT has_schema_privilege('public', 'USAGE') AND
                    (SELECT bool_and(COALESCE(has_table_privilege(to_regclass(t.name), p.name), false))
                     FROM (VALUES ('public.orders'), ('public.order_items'), ('public.order_addresses'),
                        ('public.order_status_history'), ('public.inventory_reservations'), ('public.promotions'),
                        ('public.promotion_rules'), ('public.promotion_actions'), ('public.coupons'),
                        ('public.coupon_usages'), ('public.support_tickets'), ('public.support_messages'),
                        ('public.product_review_baselines'), ('public.seller_applications'),
                        ('public.customer_preferences'), ('public.notification_receipts'), ('public.feature_flags'), ('public.reviews'), ('public.review_reply_templates'), ('public.delivery_quotes'), ('public.order_shipments'), ('public.customer_notification_settings'), ('public.customer_notifications'), ({externalLoginTable})) AS t(name)
                     CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE')) AS p(name)) AS "Value"
                """).SingleAsync(timeout.Token);
            if (!privileges) throw new InvalidOperationException("The API database account lacks operational table privileges.");
            // Check actual schema reads, not only the history marker.
            await db.Orders.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.Promotions.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.SupportTickets.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.SupportMessages.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.ProductReviewBaselines.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.AuthenticationChallenges.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.SellerApplications.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.CustomerPreferences.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.NotificationReceipts.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.FeatureFlags.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.Reviews.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.ReviewReplyTemplates.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.DeliveryQuotes.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.OrderShipments.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.CustomerNotificationSettings.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.CustomerNotifications.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.UserExternalLogins.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            await db.TelegramGuardChallenges.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            if (migrate)
            {
                await ImportCatalogDeployment.ApplyAsync(db, timeout.Token);
                Console.WriteLine("Import catalog categories and mappings: ready.");
                var catalog = await RozetkaCatalogDeployment.ApplyAsync(db, RozetkaCatalogDeployment.LoadSnapshot(), timeout.Token);
                Console.WriteLine($"Rozetka category snapshot ready: added {catalog.AddedCategories} categories, {catalog.AddedMappings} mappings, {catalog.UpdatedImages} images; preserved {catalog.FullCatalogSellers} full-catalog seller scopes.");
            }
            Console.WriteLine("Operational schema and API database privileges: ready.");
            return 0;
        }
        catch (InvalidOperationException e) { Console.Error.WriteLine("Operational deployment check failed: " + e.Message); }
        catch (Exception e)
        {
            // Do not expose connection strings, account credentials or customer data in CI logs.
            Console.Error.WriteLine("Operational deployment check failed (" + e.GetType().Name + "). Inspect private database logs.");
        }
        return 1;
    }
}
