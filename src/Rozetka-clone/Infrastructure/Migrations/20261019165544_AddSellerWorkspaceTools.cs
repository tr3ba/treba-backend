using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerWorkspaceTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_identities",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManufacturerPartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedPartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_identities", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_product_identities_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seller_import_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PreviewId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_import_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seller_import_jobs_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_seller_import_jobs_product_import_drafts_PreviewId",
                        column: x => x.PreviewId,
                        principalTable: "product_import_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_seller_import_jobs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedRole = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpectedStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Text = table.Column<string>(type: "character varying(3500)", maxLength: 3500, nullable: false),
                    View = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_notifications_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_telegram_notifications_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_identities_NormalizedPartNumber",
                table: "product_identities",
                column: "NormalizedPartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_seller_import_jobs_CategoryId",
                table: "seller_import_jobs",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_seller_import_jobs_PreviewId",
                table: "seller_import_jobs",
                column: "PreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_seller_import_jobs_Status_LeaseUntil",
                table: "seller_import_jobs",
                columns: new[] { "Status", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_seller_import_jobs_UserId_CreatedAt",
                table: "seller_import_jobs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_notifications_CompletedAt_AvailableAt",
                table: "telegram_notifications",
                columns: new[] { "CompletedAt", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_notifications_ProductId",
                table: "telegram_notifications",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_notifications_UserId",
                table: "telegram_notifications",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_identities");

            migrationBuilder.DropTable(
                name: "seller_import_jobs");

            migrationBuilder.DropTable(
                name: "telegram_notifications");
        }
    }
}
