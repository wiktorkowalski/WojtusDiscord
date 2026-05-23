using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAtUtcDropVoiceStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voice_states");

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "webhooks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "stickers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "stage_instances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "roles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "invites",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "integrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "guild_scheduled_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "emotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "channels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "auto_mod_rules",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "webhooks");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "stickers");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "stage_instances");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "invites");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "guild_scheduled_events");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "emotes");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "auto_mod_rules");

            migrationBuilder.CreateTable(
                name: "voice_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_seen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_self_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_self_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_server_deafened = table.Column<bool>(type: "boolean", nullable: false),
                    is_server_muted = table.Column<bool>(type: "boolean", nullable: false),
                    is_streaming = table.Column<bool>(type: "boolean", nullable: false),
                    is_suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    is_video = table.Column<bool>(type: "boolean", nullable: false),
                    joined_channel_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voice_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_voice_states_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_voice_states_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_voice_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_voice_states_channel_id",
                table: "voice_states",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_states_guild_id",
                table: "voice_states",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_states_user_id_guild_id",
                table: "voice_states",
                columns: new[] { "user_id", "guild_id" },
                unique: true);
        }
    }
}
