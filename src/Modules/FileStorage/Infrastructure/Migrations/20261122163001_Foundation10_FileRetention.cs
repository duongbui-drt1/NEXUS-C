using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Modules.FileStorage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Foundation10_FileRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetentionDays",
                schema: "file_storage",
                table: "stored_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetentionExpiresAt",
                schema: "file_storage",
                table: "stored_files",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetentionDays",
                schema: "file_storage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "RetentionExpiresAt",
                schema: "file_storage",
                table: "stored_files");
        }
    }
}
