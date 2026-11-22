using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_ClerkIdentitySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                schema: "identity",
                table: "external_identities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBanned",
                schema: "identity",
                table: "external_identities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                schema: "identity",
                table: "external_identities",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastProviderEventAt",
                schema: "identity",
                table: "external_identities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncStatus",
                schema: "identity",
                table: "external_identities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Synced");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncedAt",
                schema: "identity",
                table: "external_identities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "clerk_webhook_events",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SvixId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EventTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clerk_webhook_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clerk_webhook_events_ReceivedAt",
                schema: "identity",
                table: "clerk_webhook_events",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_clerk_webhook_events_SvixId",
                schema: "identity",
                table: "clerk_webhook_events",
                column: "SvixId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clerk_webhook_events",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                schema: "identity",
                table: "external_identities");

            migrationBuilder.DropColumn(
                name: "IsBanned",
                schema: "identity",
                table: "external_identities");

            migrationBuilder.DropColumn(
                name: "LastError",
                schema: "identity",
                table: "external_identities");

            migrationBuilder.DropColumn(
                name: "LastProviderEventAt",
                schema: "identity",
                table: "external_identities");

            migrationBuilder.DropColumn(
                name: "SyncStatus",
                schema: "identity",
                table: "external_identities");

            migrationBuilder.DropColumn(
                name: "SyncedAt",
                schema: "identity",
                table: "external_identities");
        }
    }
}
