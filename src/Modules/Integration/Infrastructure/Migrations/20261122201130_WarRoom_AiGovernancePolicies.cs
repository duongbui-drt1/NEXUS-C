using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Integration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_AiGovernancePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_capability_policies",
                schema: "integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    KillSwitchActive = table.Column<bool>(type: "boolean", nullable: false),
                    RequireHumanApproval = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyTokenBudget = table.Column<long>(type: "bigint", nullable: false),
                    AllowedTools = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PromptTemplateVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "v1"),
                    DataClassification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Internal"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_capability_policies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_capability_policies_TenantId_CapabilityCode",
                schema: "integration",
                table: "ai_capability_policies",
                columns: new[] { "TenantId", "CapabilityCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_capability_policies",
                schema: "integration");
        }
    }
}
