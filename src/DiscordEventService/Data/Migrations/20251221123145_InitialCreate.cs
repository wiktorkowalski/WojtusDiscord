using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_rules_guilds_guild_id",
                table: "auto_mod_rules");

            migrationBuilder.AlterColumn<Guid>(
                name: "message_id",
                table: "message_edit_history",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "guild_id",
                table: "auto_mod_rules",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "creator_id",
                table: "auto_mod_rules",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_event_type",
                table: "failed_events",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_failed_at_utc",
                table: "failed_events",
                column: "failed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_guild_discord_id",
                table: "failed_events",
                column: "guild_discord_id");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_is_resolved",
                table: "failed_events",
                column: "is_resolved");

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_is_resolved_failed_at_utc",
                table: "failed_events",
                columns: new[] { "is_resolved", "failed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_auto_mod_rules_creator_id",
                table: "auto_mod_rules",
                column: "creator_id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_rules_guilds_guild_id",
                table: "auto_mod_rules",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_rules_users_creator_id",
                table: "auto_mod_rules",
                column: "creator_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_rules_guilds_guild_id",
                table: "auto_mod_rules");

            migrationBuilder.DropForeignKey(
                name: "fk_auto_mod_rules_users_creator_id",
                table: "auto_mod_rules");

            migrationBuilder.DropIndex(
                name: "ix_failed_events_event_type",
                table: "failed_events");

            migrationBuilder.DropIndex(
                name: "ix_failed_events_failed_at_utc",
                table: "failed_events");

            migrationBuilder.DropIndex(
                name: "ix_failed_events_guild_discord_id",
                table: "failed_events");

            migrationBuilder.DropIndex(
                name: "ix_failed_events_is_resolved",
                table: "failed_events");

            migrationBuilder.DropIndex(
                name: "ix_failed_events_is_resolved_failed_at_utc",
                table: "failed_events");

            migrationBuilder.DropIndex(
                name: "ix_auto_mod_rules_creator_id",
                table: "auto_mod_rules");

            migrationBuilder.AlterColumn<Guid>(
                name: "message_id",
                table: "message_edit_history",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "guild_id",
                table: "auto_mod_rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "creator_id",
                table: "auto_mod_rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_auto_mod_rules_guilds_guild_id",
                table: "auto_mod_rules",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
