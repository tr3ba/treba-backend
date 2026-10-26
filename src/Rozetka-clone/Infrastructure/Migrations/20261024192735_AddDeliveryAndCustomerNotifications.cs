using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryAndCustomerNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_notification_settings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdersEmail = table.Column<bool>(type: "boolean", nullable: false),
                    OrdersTelegram = table.Column<bool>(type: "boolean", nullable: false),
                    SupportEmail = table.Column<bool>(type: "boolean", nullable: false),
                    SupportTelegram = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_notification_settings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_customer_notification_settings_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_notifications_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CityRef = table.Column<Guid>(type: "uuid", nullable: false),
                    PickupPointRef = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PickupPoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Postomat = table.Column<bool>(type: "boolean", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DeclaredValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    EstimatedWeight = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedByOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_quotes_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_shipments",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityRef = table.Column<Guid>(type: "uuid", nullable: false),
                    PickupPointRef = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PickupPoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Postomat = table.Column<bool>(type: "boolean", nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QuotedPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    EstimatedWeight = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_shipments", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_order_shipments_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_notifications_CompletedAt_AvailableAt",
                table: "customer_notifications",
                columns: new[] { "CompletedAt", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_notifications_EventKey_UserId_Channel",
                table: "customer_notifications",
                columns: new[] { "EventKey", "UserId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_notifications_UserId",
                table: "customer_notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_quotes_ExpiresAt",
                table: "delivery_quotes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_quotes_UserId",
                table: "delivery_quotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_order_shipments_Reference",
                table: "order_shipments",
                column: "Reference",
                unique: true);
            // Copy only existing application CRUD grants; never grant access to PUBLIC.
            migrationBuilder.Sql("""
                DO $grants$
                DECLARE permission record; destination text;
                BEGIN
                    FOR permission IN SELECT DISTINCT grantee, privilege_type
                        FROM information_schema.role_table_grants
                        WHERE table_schema = current_schema() AND table_name = 'orders'
                            AND grantee <> 'PUBLIC' AND privilege_type IN ('SELECT', 'INSERT', 'UPDATE', 'DELETE')
                    LOOP
                        FOREACH destination IN ARRAY ARRAY['delivery_quotes', 'order_shipments', 'customer_notification_settings', 'customer_notifications']
                        LOOP
                            EXECUTE format('GRANT %s ON TABLE %I.%I TO %I', permission.privilege_type, current_schema(), destination, permission.grantee);
                        END LOOP;
                    END LOOP;
                END $grants$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_notification_settings");

            migrationBuilder.DropTable(
                name: "customer_notifications");

            migrationBuilder.DropTable(
                name: "delivery_quotes");

            migrationBuilder.DropTable(
                name: "order_shipments");
        }
    }
}
