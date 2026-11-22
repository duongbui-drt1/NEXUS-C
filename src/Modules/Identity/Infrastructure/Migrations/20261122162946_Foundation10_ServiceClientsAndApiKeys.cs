using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Foundation10_ServiceClientsAndApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    KeyPrefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AllowedScopes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "service_clients",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApplicationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecretSalt = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClientSecretHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SecretPrefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AllowedScopes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecretRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_clients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_KeyHash",
                schema: "identity",
                table: "api_keys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_TenantId",
                schema: "identity",
                table: "api_keys",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_service_clients_ClientId",
                schema: "identity",
                table: "service_clients",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_clients_TenantId",
                schema: "identity",
                table: "service_clients",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "service_clients",
                schema: "identity");
        }
    }
}
