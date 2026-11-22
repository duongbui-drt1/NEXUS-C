using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.AccessControl.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WarRoom_AppScopedAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationCode",
                schema: "access_control",
                table: "roles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSharedReusable",
                schema: "access_control",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationCode",
                schema: "access_control",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "IsSharedReusable",
                schema: "access_control",
                table: "roles");
        }
    }
}
