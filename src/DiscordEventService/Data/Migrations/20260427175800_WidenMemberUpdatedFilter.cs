using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class WidenMemberUpdatedFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "guild_avatar_hash_after",
                table: "member_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guild_avatar_hash_before",
                table: "member_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deafened_after",
                table: "member_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deafened_before",
                table: "member_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_muted_after",
                table: "member_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_muted_before",
                table: "member_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_pending_after",
                table: "member_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_pending_before",
                table: "member_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "premium_since_after",
                table: "member_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "premium_since_before",
                table: "member_events",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "guild_avatar_hash_after",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "guild_avatar_hash_before",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "is_deafened_after",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "is_deafened_before",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "is_muted_after",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "is_muted_before",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "is_pending_after",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "is_pending_before",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "premium_since_after",
                table: "member_events");

            migrationBuilder.DropColumn(
                name: "premium_since_before",
                table: "member_events");
        }
    }
}
