using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CustomerId",
                table: "support_tickets");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "support_tickets",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustomerReadAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "support_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StaffReadAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CustomerId",
                table: "support_tickets",
                column: "CustomerId",
                unique: true,
                filter: "\"Kind\" = 'CHAT' AND \"Status\" <> 'CLOSED'");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CustomerId_ProductId",
                table: "support_tickets",
                columns: new[] { "CustomerId", "ProductId" },
                unique: true,
                filter: "\"Kind\" = 'QUESTION' AND \"ProductId\" IS NOT NULL AND \"Status\" <> 'CLOSED'");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CustomerId_UpdatedAt",
                table: "support_tickets",
                columns: new[] { "CustomerId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_ProductId",
                table: "support_tickets",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_support_tickets_products_ProductId",
                table: "support_tickets",
                column: "ProductId",
                principalTable: "products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $support_rollback$ BEGIN
                    IF EXISTS (SELECT 1 FROM support_tickets WHERE "OrderId" IS NULL) THEN
                        RAISE EXCEPTION 'Support conversations without orders exist. Archive them explicitly before reverting this schema.';
                    END IF;
                END $support_rollback$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_support_tickets_products_ProductId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CustomerId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CustomerId_ProductId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CustomerId_UpdatedAt",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_ProductId",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "CustomerReadAt",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "StaffReadAt",
                table: "support_tickets");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "support_tickets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CustomerId",
                table: "support_tickets",
                column: "CustomerId");
        }
    }
}
