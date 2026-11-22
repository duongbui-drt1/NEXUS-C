using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.BuildingBlocks.Messaging.Migrations
{
    /// <inheritdoc />
    public partial class Foundation10_MessagingOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CausationId",
                schema: "messaging",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Producer",
                schema: "messaging",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "nexus-c");

            migrationBuilder.AddColumn<string>(
                name: "CausationId",
                schema: "messaging",
                table: "dead_letter_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsResolved",
                schema: "messaging",
                table: "dead_letter_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRetriedAt",
                schema: "messaging",
                table: "dead_letter_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastRetriedByUserId",
                schema: "messaging",
                table: "dead_letter_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManualRetryCount",
                schema: "messaging",
                table: "dead_letter_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Producer",
                schema: "messaging",
                table: "dead_letter_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "nexus-c");

            migrationBuilder.AddColumn<string>(
                name: "RetryHistoryJson",
                schema: "messaging",
                table: "dead_letter_messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_messages_IsResolved",
                schema: "messaging",
                table: "dead_letter_messages",
                column: "IsResolved");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_dead_letter_messages_IsResolved",
                schema: "messaging",
                table: "dead_letter_messages");

            migrationBuilder.DropColumn(
                name: "CausationId",
                schema: "messaging",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "Producer",
                schema: "messaging",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "CausationId",
                schema: "messaging",
                table: "dead_letter_messages");

            migrationBuilder.DropColumn(
                name: "IsResolved",
                schema: "messaging",
                table: "dead_letter_messages");

            migrationBuilder.DropColumn(
                name: "LastRetriedAt",
                schema: "messaging",
                table: "dead_letter_messages");

            migrationBuilder.DropColumn(
                name: "LastRetriedByUserId",
                schema: "messaging",
                table: "dead_letter_messages");

            migrationBuilder.DropColumn(
                name: "ManualRetryCount",
                schema: "messaging",
                table: "dead_letter_messages");

            migrationBuilder.DropColumn(
                name: "Producer",
                schema: "messaging",
                table: "dead_letter_messages");

            migrationBuilder.DropColumn(
                name: "RetryHistoryJson",
                schema: "messaging",
                table: "dead_letter_messages");
        }
    }
}
