using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsiteAdminLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LoginChallengeId",
                table: "telegram_guard_challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_guard_challenges_LoginChallengeId",
                table: "telegram_guard_challenges",
                column: "LoginChallengeId");

            migrationBuilder.AddForeignKey(
                name: "FK_telegram_guard_challenges_authentication_challenges_LoginCh~",
                table: "telegram_guard_challenges",
                column: "LoginChallengeId",
                principalTable: "authentication_challenges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_telegram_guard_challenges_authentication_challenges_LoginCh~",
                table: "telegram_guard_challenges");

            migrationBuilder.DropIndex(
                name: "IX_telegram_guard_challenges_LoginChallengeId",
                table: "telegram_guard_challenges");

            migrationBuilder.DropColumn(
                name: "LoginChallengeId",
                table: "telegram_guard_challenges");
        }
    }
}
