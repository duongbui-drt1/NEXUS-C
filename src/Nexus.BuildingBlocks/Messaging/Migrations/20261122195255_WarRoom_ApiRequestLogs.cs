using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.BuildingBlocks.Messaging.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_ApiRequestLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_request_logs",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NormalizedRoute = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApplicationCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ServiceClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RateLimitDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RateLimitPolicy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_request_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_request_logs_CorrelationId",
                schema: "messaging",
                table: "api_request_logs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_api_request_logs_StatusCode_Timestamp",
                schema: "messaging",
                table: "api_request_logs",
                columns: new[] { "StatusCode", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_api_request_logs_TenantId_Timestamp",
                schema: "messaging",
                table: "api_request_logs",
                columns: new[] { "TenantId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_api_request_logs_Timestamp",
                schema: "messaging",
                table: "api_request_logs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_request_logs",
                schema: "messaging");
        }
    }
}
