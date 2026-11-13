using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImageModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_product_images_ProductId",
                table: "product_images");

            migrationBuilder.CreateTable(
                name: "product_image_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AltText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: true),
                    IsMain = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_image_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_image_changes_product_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "product_images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_product_image_changes_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_product_image_changes_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_image_changes_users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_images_ProductId",
                table: "product_images",
                column: "ProductId",
                unique: true,
                filter: "\"IsMain\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_product_image_changes_ImageId",
                table: "product_image_changes",
                column: "ImageId",
                unique: true,
                filter: "\"ImageId\" IS NOT NULL AND \"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_product_image_changes_ImageId_Status",
                table: "product_image_changes",
                columns: new[] { "ImageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_product_image_changes_ProductId_Status",
                table: "product_image_changes",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_product_image_changes_ReviewedByUserId",
                table: "product_image_changes",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_product_image_changes_VariantId",
                table: "product_image_changes",
                column: "VariantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_image_changes");

            migrationBuilder.DropIndex(
                name: "IX_product_images_ProductId",
                table: "product_images");

            migrationBuilder.CreateIndex(
                name: "IX_product_images_ProductId",
                table: "product_images",
                column: "ProductId");
        }
    }
}
