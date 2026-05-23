using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_webhooks_soft_delete",
                table: "webhooks",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stickers_soft_delete",
                table: "stickers",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stage_instances_soft_delete",
                table: "stage_instances",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_roles_soft_delete",
                table: "roles",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_messages_soft_delete",
                table: "messages",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invites_soft_delete",
                table: "invites",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_integrations_soft_delete",
                table: "integrations",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_guild_scheduled_events_soft_delete",
                table: "guild_scheduled_events",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_emotes_soft_delete",
                table: "emotes",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_channels_soft_delete",
                table: "channels",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_mod_rules_soft_delete",
                table: "auto_mod_rules",
                sql: "(is_deleted = false AND deleted_at_utc IS NULL) OR (is_deleted = true AND deleted_at_utc IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_webhooks_soft_delete",
                table: "webhooks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stickers_soft_delete",
                table: "stickers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stage_instances_soft_delete",
                table: "stage_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_roles_soft_delete",
                table: "roles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_messages_soft_delete",
                table: "messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invites_soft_delete",
                table: "invites");

            migrationBuilder.DropCheckConstraint(
                name: "ck_integrations_soft_delete",
                table: "integrations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_guild_scheduled_events_soft_delete",
                table: "guild_scheduled_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_emotes_soft_delete",
                table: "emotes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_channels_soft_delete",
                table: "channels");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_mod_rules_soft_delete",
                table: "auto_mod_rules");
        }
    }
}
