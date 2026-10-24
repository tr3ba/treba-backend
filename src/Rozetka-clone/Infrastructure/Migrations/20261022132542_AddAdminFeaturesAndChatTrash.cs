using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminFeaturesAndChatTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CustomerId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CustomerId_ProductId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_OrderId_Kind",
                table: "support_tickets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "support_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "support_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "support_tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "feature_flags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_flags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CustomerId",
                table: "support_tickets",
                column: "CustomerId",
                unique: true,
                filter: "NOT \"IsDeleted\" AND \"Kind\" = 'CHAT' AND \"Status\" <> 'CLOSED'");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CustomerId_ProductId",
                table: "support_tickets",
                columns: new[] { "CustomerId", "ProductId" },
                unique: true,
                filter: "NOT \"IsDeleted\" AND \"Kind\" = 'QUESTION' AND \"ProductId\" IS NOT NULL AND \"Status\" <> 'CLOSED'");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_OrderId_Kind",
                table: "support_tickets",
                columns: new[] { "OrderId", "Kind" },
                unique: true,
                filter: "NOT \"IsDeleted\" AND \"Kind\" = 'RETURN' AND \"Status\" IN ('OPEN', 'IN_PROGRESS', 'APPROVED')");

            migrationBuilder.CreateIndex(
                name: "IX_feature_flags_Key",
                table: "feature_flags",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_flags");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CustomerId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_CustomerId_ProductId",
                table: "support_tickets");

            migrationBuilder.DropIndex(
                name: "IX_support_tickets_OrderId_Kind",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "support_tickets");

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
                name: "IX_support_tickets_OrderId_Kind",
                table: "support_tickets",
                columns: new[] { "OrderId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'RETURN' AND \"Status\" IN ('OPEN', 'IN_PROGRESS', 'APPROVED')");
        }
    }
}
