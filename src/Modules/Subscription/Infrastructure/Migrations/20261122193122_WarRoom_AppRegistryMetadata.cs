using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Subscription.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_AppRegistryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedAuthMethods",
                schema: "subscription",
                table: "applications",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "jwt,oauth2_client_credentials");

            migrationBuilder.AddColumn<string>(
                name: "Owner",
                schema: "subscription",
                table: "applications",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "NEXUS Platform");

            migrationBuilder.AddColumn<string>(
                name: "RedirectOrigins",
                schema: "subscription",
                table: "applications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrustClassification",
                schema: "subscription",
                table: "applications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "FirstParty");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedAuthMethods",
                schema: "subscription",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "Owner",
                schema: "subscription",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "RedirectOrigins",
                schema: "subscription",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "TrustClassification",
                schema: "subscription",
                table: "applications");
        }
    }
}
