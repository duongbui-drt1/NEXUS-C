using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Workflow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Foundation10_WorkflowGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalMode",
                schema: "workflow",
                table: "versions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Sequential");

            migrationBuilder.AddColumn<string>(
                name: "ConditionExpressionJson",
                schema: "workflow",
                table: "versions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EscalationHours",
                schema: "workflow",
                table: "versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationRoleName",
                schema: "workflow",
                table: "versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireMakerChecker",
                schema: "workflow",
                table: "versions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedUserId",
                schema: "workflow",
                table: "approval_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DelegatedFromUserId",
                schema: "workflow",
                table: "approval_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EscalatedAt",
                schema: "workflow",
                table: "approval_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalatedToRoleName",
                schema: "workflow",
                table: "approval_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SlaDueAt",
                schema: "workflow",
                table: "approval_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "approval_delegations",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_delegations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approval_delegations_TenantId_DelegateUserId_IsActive",
                schema: "workflow",
                table: "approval_delegations",
                columns: new[] { "TenantId", "DelegateUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_delegations_TenantId_DelegatorUserId_IsActive",
                schema: "workflow",
                table: "approval_delegations",
                columns: new[] { "TenantId", "DelegatorUserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_delegations",
                schema: "workflow");

            migrationBuilder.DropColumn(
                name: "ApprovalMode",
                schema: "workflow",
                table: "versions");

            migrationBuilder.DropColumn(
                name: "ConditionExpressionJson",
                schema: "workflow",
                table: "versions");

            migrationBuilder.DropColumn(
                name: "EscalationHours",
                schema: "workflow",
                table: "versions");

            migrationBuilder.DropColumn(
                name: "EscalationRoleName",
                schema: "workflow",
                table: "versions");

            migrationBuilder.DropColumn(
                name: "RequireMakerChecker",
                schema: "workflow",
                table: "versions");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                schema: "workflow",
                table: "approval_tasks");

            migrationBuilder.DropColumn(
                name: "DelegatedFromUserId",
                schema: "workflow",
                table: "approval_tasks");

            migrationBuilder.DropColumn(
                name: "EscalatedAt",
                schema: "workflow",
                table: "approval_tasks");

            migrationBuilder.DropColumn(
                name: "EscalatedToRoleName",
                schema: "workflow",
                table: "approval_tasks");

            migrationBuilder.DropColumn(
                name: "SlaDueAt",
                schema: "workflow",
                table: "approval_tasks");
        }
    }
}
