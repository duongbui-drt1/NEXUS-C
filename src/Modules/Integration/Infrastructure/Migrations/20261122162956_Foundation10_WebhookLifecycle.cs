using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Integration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Foundation10_WebhookLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "integration",
                table: "webhooks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecretPrefix",
                schema: "integration",
                table: "webhooks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SecretRotatedAt",
                schema: "integration",
                table: "webhooks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecretVersion",
                schema: "integration",
                table: "webhooks",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SigningSecretHash",
                schema: "integration",
                table: "webhooks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    TimestampUnix = table.Column<long>(type: "bigint", nullable: false),
                    SignatureDigestPrefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_webhook_deliveries_webhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalSchema: "integration",
                        principalTable: "webhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_TenantId_WebhookId_AttemptedAt",
                schema: "integration",
                table: "webhook_deliveries",
                columns: new[] { "TenantId", "WebhookId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_WebhookId",
                schema: "integration",
                table: "webhook_deliveries",
                column: "WebhookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "integration");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "integration",
                table: "webhooks");

            migrationBuilder.DropColumn(
                name: "SecretPrefix",
                schema: "integration",
                table: "webhooks");

            migrationBuilder.DropColumn(
                name: "SecretRotatedAt",
                schema: "integration",
                table: "webhooks");

            migrationBuilder.DropColumn(
                name: "SecretVersion",
                schema: "integration",
                table: "webhooks");

            migrationBuilder.DropColumn(
                name: "SigningSecretHash",
                schema: "integration",
                table: "webhooks");
        }
    }
}
