using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedShipmentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Reference",
                table: "order_shipments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "Carrier",
                table: "order_shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NovaPoshta");

            migrationBuilder.Sql("""
                UPDATE order_shipments s SET "Reference" = NULL
                FROM orders o WHERE o."Id" = s."OrderId" AND o."Status" IN ('CREATED', 'AWAITING_PAYMENT');
                UPDATE order_shipments SET "Reference" = 'TRB-' || substring("Reference" from 12)
                WHERE "Reference" LIKE 'TREBA-DEMO-%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE order_shipments SET "Reference" = 'TREBA-DOWNGRADE-' || replace("OrderId"::text, '-', '')
                WHERE "Reference" IS NULL;
                """);
            migrationBuilder.DropColumn(
                name: "Carrier",
                table: "order_shipments");

            migrationBuilder.AlterColumn<string>(
                name: "Reference",
                table: "order_shipments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
