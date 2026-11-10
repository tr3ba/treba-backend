using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecureStandaloneAccountChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastAuthenticatorStep",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MfaFailedAttempts",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MfaWindowStart",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FreeDelivery",
                table: "delivery_quotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AccountEmailSnapshot",
                table: "authentication_challenges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CodeSendCount",
                table: "authentication_challenges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationPasswordHash",
                table: "authentication_challenges",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryCodeHash",
                table: "authentication_challenges",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAuthenticatorStep",
                table: "users");

            migrationBuilder.DropColumn(
                name: "MfaFailedAttempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "MfaWindowStart",
                table: "users");

            migrationBuilder.DropColumn(
                name: "FreeDelivery",
                table: "delivery_quotes");

            migrationBuilder.DropColumn(
                name: "AccountEmailSnapshot",
                table: "authentication_challenges");

            migrationBuilder.DropColumn(
                name: "CodeSendCount",
                table: "authentication_challenges");

            migrationBuilder.DropColumn(
                name: "RegistrationPasswordHash",
                table: "authentication_challenges");

            migrationBuilder.DropColumn(
                name: "SecondaryCodeHash",
                table: "authentication_challenges");
        }
    }
}
