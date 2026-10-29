using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "authentication_challenges",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_authentication_challenges_DeliveryAddress_LastCodeSentAt",
                table: "authentication_challenges",
                columns: new[] { "DeliveryAddress", "LastCodeSentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_authentication_challenges_DeliveryAddress_LastCodeSentAt",
                table: "authentication_challenges");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "authentication_challenges");
        }
    }
}
