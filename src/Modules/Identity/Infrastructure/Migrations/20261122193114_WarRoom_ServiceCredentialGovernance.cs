using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_ServiceCredentialGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CredentialClass",
                schema: "identity",
                table: "service_clients",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Application");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                schema: "identity",
                table: "service_clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousSecretHash",
                schema: "identity",
                table: "service_clients",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousSecretSalt",
                schema: "identity",
                table: "service_clients",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreviousSecretValidUntil",
                schema: "identity",
                table: "service_clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationCode",
                schema: "identity",
                table: "api_keys",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CredentialClass",
                schema: "identity",
                table: "api_keys",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Application");

            migrationBuilder.AddColumn<string>(
                name: "PreviousKeyHash",
                schema: "identity",
                table: "api_keys",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreviousKeyValidUntil",
                schema: "identity",
                table: "api_keys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceClientId",
                schema: "identity",
                table: "api_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_clients_ApplicationCode",
                schema: "identity",
                table: "service_clients",
                column: "ApplicationCode");

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_ApplicationCode",
                schema: "identity",
                table: "api_keys",
                column: "ApplicationCode");

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_PreviousKeyHash",
                schema: "identity",
                table: "api_keys",
                column: "PreviousKeyHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_service_clients_ApplicationCode",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropIndex(
                name: "IX_api_keys_ApplicationCode",
                schema: "identity",
                table: "api_keys");

            migrationBuilder.DropIndex(
                name: "IX_api_keys_PreviousKeyHash",
                schema: "identity",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "CredentialClass",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropColumn(
                name: "PreviousSecretHash",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropColumn(
                name: "PreviousSecretSalt",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropColumn(
                name: "PreviousSecretValidUntil",
                schema: "identity",
                table: "service_clients");

            migrationBuilder.DropColumn(
                name: "ApplicationCode",
                schema: "identity",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "CredentialClass",
                schema: "identity",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "PreviousKeyHash",
                schema: "identity",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "PreviousKeyValidUntil",
                schema: "identity",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "ServiceClientId",
                schema: "identity",
                table: "api_keys");
        }
    }
}
