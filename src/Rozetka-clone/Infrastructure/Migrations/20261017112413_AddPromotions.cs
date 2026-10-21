using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Stackable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UsageLimit = table.Column<int>(type: "integer", nullable: false),
                    UsageLimitPerUser = table.Column<int>(type: "integer", nullable: false),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_coupons_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_actions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumDiscount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_actions_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RuleValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_promotion_rules_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coupon_usages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_usages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_coupon_usages_coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coupon_usages_CouponId",
                table: "coupon_usages",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_coupon_usages_CouponId_OrderId",
                table: "coupon_usages",
                columns: new[] { "CouponId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coupon_usages_OrderId",
                table: "coupon_usages",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_coupon_usages_UserId",
                table: "coupon_usages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_coupons_Active",
                table: "coupons",
                column: "Active");

            migrationBuilder.CreateIndex(
                name: "IX_coupons_Code",
                table: "coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coupons_PromotionId",
                table: "coupons",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_actions_PromotionId",
                table: "promotion_actions",
                column: "PromotionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rules_PromotionId",
                table: "promotion_rules",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rules_PromotionId_RuleType",
                table: "promotion_rules",
                columns: new[] { "PromotionId", "RuleType" });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_Priority",
                table: "promotions",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_promotions_StartAt_EndAt",
                table: "promotions",
                columns: new[] { "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_Status",
                table: "promotions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coupon_usages");

            migrationBuilder.DropTable(
                name: "promotion_actions");

            migrationBuilder.DropTable(
                name: "promotion_rules");

            migrationBuilder.DropTable(
                name: "coupons");

            migrationBuilder.DropTable(
                name: "promotions");
        }
    }
}
