using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Subscription.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Foundation10_ApplicationRegistryAndEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                schema: "subscription",
                table: "usage_records",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "subscription",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "subscription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CurrentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReleaseChannel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MinPlatformVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequiredCapabilities = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ModuleDependencies = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CompatibilityMetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_applications",
                schema: "subscription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActivated = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_applications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_TenantId_IdempotencyKey",
                schema: "subscription",
                table: "usage_records",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_applications_Code",
                schema: "subscription",
                table: "applications",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_applications_TenantId_ApplicationCode",
                schema: "subscription",
                table: "tenant_applications",
                columns: new[] { "TenantId", "ApplicationCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applications",
                schema: "subscription");

            migrationBuilder.DropTable(
                name: "tenant_applications",
                schema: "subscription");

            migrationBuilder.DropIndex(
                name: "IX_usage_records_TenantId_IdempotencyKey",
                schema: "subscription",
                table: "usage_records");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                schema: "subscription",
                table: "usage_records");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "subscription",
                table: "plans");
        }
    }
}
