using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Organization.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Foundation10_OrganizationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveFrom",
                schema: "organization",
                table: "user_organization_assignments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveTo",
                schema: "organization",
                table: "user_organization_assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "organization",
                table: "user_organization_assignments",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "organization",
                table: "organization_units",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                schema: "organization",
                table: "user_organization_assignments");

            migrationBuilder.DropColumn(
                name: "EffectiveTo",
                schema: "organization",
                table: "user_organization_assignments");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "organization",
                table: "user_organization_assignments");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "organization",
                table: "organization_units");
        }
    }
}
