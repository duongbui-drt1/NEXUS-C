using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Integration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_ProviderCatalogAndWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationCode",
                schema: "integration",
                table: "webhooks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DurationMs",
                schema: "integration",
                table: "webhook_deliveries",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseSnippet",
                schema: "integration",
                table: "webhook_deliveries",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedHosts",
                schema: "integration",
                table: "providers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfigSchemaJson",
                schema: "integration",
                table: "providers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresSecretRef",
                schema: "integration",
                table: "providers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                schema: "integration",
                table: "providers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "1.0");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationCode",
                schema: "integration",
                table: "connections",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DisabledAt",
                schema: "integration",
                table: "connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthStatus",
                schema: "integration",
                table: "connections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHealthCheckAt",
                schema: "integration",
                table: "connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NonSecretConfigJson",
                schema: "integration",
                table: "connections",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationCode",
                schema: "integration",
                table: "webhooks");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                schema: "integration",
                table: "webhook_deliveries");

            migrationBuilder.DropColumn(
                name: "ResponseSnippet",
                schema: "integration",
                table: "webhook_deliveries");

            migrationBuilder.DropColumn(
                name: "AllowedHosts",
                schema: "integration",
                table: "providers");

            migrationBuilder.DropColumn(
                name: "ConfigSchemaJson",
                schema: "integration",
                table: "providers");

            migrationBuilder.DropColumn(
                name: "RequiresSecretRef",
                schema: "integration",
                table: "providers");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "integration",
                table: "providers");

            migrationBuilder.DropColumn(
                name: "ApplicationCode",
                schema: "integration",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "DisabledAt",
                schema: "integration",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "HealthStatus",
                schema: "integration",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckAt",
                schema: "integration",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "NonSecretConfigJson",
                schema: "integration",
                table: "connections");
        }
    }
}
