using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_import_drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceProductId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    SourceCategoryKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceCategoryName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    BrandName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ImagesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CharacteristicsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourcePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_import_drafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_import_drafts_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_import_drafts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rozetka_category_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rozetka_category_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rozetka_category_mappings_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "seller_category_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncludeDescendants = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_category_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seller_category_grants_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_seller_category_grants_sellers_SellerId",
                        column: x => x.SellerId,
                        principalTable: "sellers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_bindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_bindings_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_bot_cursors",
                columns: table => new
                {
                    BotId = table.Column<long>(type: "bigint", nullable: false),
                    LastUpdateId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_bot_cursors", x => x.BotId);
                });

            migrationBuilder.CreateTable(
                name: "telegram_guard_challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrowserSecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovalSecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayCode = table.Column<int>(type: "integer", nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedTelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_guard_challenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_guard_challenges_users_ApprovedUserId",
                        column: x => x.ApprovedUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_link_challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    ChatId = table.Column<long>(type: "bigint", nullable: true),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TelegramName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_link_challenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_link_challenges_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_import_drafts_CategoryId",
                table: "product_import_drafts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_product_import_drafts_ExpiresAt",
                table: "product_import_drafts",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_product_import_drafts_UserId_SourceProductId",
                table: "product_import_drafts",
                columns: new[] { "UserId", "SourceProductId" },
                unique: true,
                filter: "\"SubmittedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_rozetka_category_mappings_CategoryId",
                table: "rozetka_category_mappings",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_rozetka_category_mappings_ExternalKey",
                table: "rozetka_category_mappings",
                column: "ExternalKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seller_category_grants_CategoryId",
                table: "seller_category_grants",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_seller_category_grants_SellerId_CategoryId",
                table: "seller_category_grants",
                columns: new[] { "SellerId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_bindings_TelegramUserId",
                table: "telegram_bindings",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_bindings_UserId",
                table: "telegram_bindings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_guard_challenges_ApprovedUserId",
                table: "telegram_guard_challenges",
                column: "ApprovedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_guard_challenges_ExpiresAt",
                table: "telegram_guard_challenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_challenges_ExpiresAt",
                table: "telegram_link_challenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_challenges_TokenHash",
                table: "telegram_link_challenges",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_challenges_UserId",
                table: "telegram_link_challenges",
                column: "UserId");

            // Preserve access to categories of the seller's existing products, without granting new descendants.
            migrationBuilder.Sql("""
                INSERT INTO seller_category_grants ("Id", "SellerId", "CategoryId", "IncludeDescendants", "CreatedAt")
                SELECT DISTINCT md5(s."Id"::text || p."CategoryId"::text)::uuid, s."Id", p."CategoryId", FALSE, CURRENT_TIMESTAMP
                FROM sellers s
                JOIN stores st ON st."SellerId" = s."Id"
                JOIN products p ON p."StoreId" = st."Id"
                JOIN categories c ON c."Id" = p."CategoryId" AND c."IsActive"
                WHERE s."Status" = 'ACTIVE'
                ON CONFLICT ("SellerId", "CategoryId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_import_drafts");

            migrationBuilder.DropTable(
                name: "rozetka_category_mappings");

            migrationBuilder.DropTable(
                name: "seller_category_grants");

            migrationBuilder.DropTable(
                name: "telegram_bindings");

            migrationBuilder.DropTable(
                name: "telegram_bot_cursors");

            migrationBuilder.DropTable(
                name: "telegram_guard_challenges");

            migrationBuilder.DropTable(
                name: "telegram_link_challenges");
        }
    }
}
