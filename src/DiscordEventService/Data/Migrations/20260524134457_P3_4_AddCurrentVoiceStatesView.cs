using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P3_4_AddCurrentVoiceStatesView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW v_current_voice_states AS
                SELECT
                    user_discord_id,
                    guild_discord_id,
                    channel_discord_id_after AS channel_discord_id,
                    is_self_muted,
                    is_self_deafened,
                    is_server_muted,
                    is_server_deafened,
                    is_streaming,
                    is_video,
                    is_suppressed,
                    session_id,
                    event_timestamp_utc
                FROM (
                    SELECT DISTINCT ON (user_discord_id, guild_discord_id) *
                    FROM voice_state_events
                    ORDER BY user_discord_id, guild_discord_id, event_timestamp_utc DESC
                ) latest
                WHERE latest.event_type != 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_current_voice_states;");
        }
    }
}
