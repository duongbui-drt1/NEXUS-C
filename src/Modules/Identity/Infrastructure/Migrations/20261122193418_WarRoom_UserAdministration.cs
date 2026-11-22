using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_UserAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSignInAt",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminNotes",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSignInAt",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AdminNotes",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "FirstName",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "LastName",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "Region",
                schema: "identity",
                table: "user_profiles");
        }
    }
}
