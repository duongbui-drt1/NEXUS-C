using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Subscription.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_SubscriptionPlanArchival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveReason",
                schema: "subscription",
                table: "plans",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                schema: "subscription",
                table: "plans",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveReason",
                schema: "subscription",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "subscription",
                table: "plans");
        }
    }
}
