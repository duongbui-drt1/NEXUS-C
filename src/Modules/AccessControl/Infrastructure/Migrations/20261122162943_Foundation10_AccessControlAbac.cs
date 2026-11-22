using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.AccessControl.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Foundation10_AccessControlAbac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_roles_UserId_RoleId_TenantId",
                schema: "access_control",
                table: "user_roles");

            migrationBuilder.AddColumn<string>(
                name: "Effect",
                schema: "access_control",
                table: "user_roles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Allow");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_UserId_RoleId_TenantId_ResourceScopeType_Resourc~",
                schema: "access_control",
                table: "user_roles",
                columns: new[] { "UserId", "RoleId", "TenantId", "ResourceScopeType", "ResourceScopeId", "Effect" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_roles_UserId_RoleId_TenantId_ResourceScopeType_Resourc~",
                schema: "access_control",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "Effect",
                schema: "access_control",
                table: "user_roles");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_UserId_RoleId_TenantId",
                schema: "access_control",
                table: "user_roles",
                columns: new[] { "UserId", "RoleId", "TenantId" },
                unique: true);
        }
    }
}
