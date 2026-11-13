using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveShipmentUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_shipments_OrderId",
                table: "shipments");

            migrationBuilder.CreateIndex(
                name: "UX_shipments_OrderId_Active",
                table: "shipments",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" IN ('CREATING', 'CREATED', 'PREPARING', 'READY', 'SHIPPED', 'IN_TRANSIT')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_shipments_OrderId_Active",
                table: "shipments");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_OrderId",
                table: "shipments",
                column: "OrderId");
        }
    }
}
