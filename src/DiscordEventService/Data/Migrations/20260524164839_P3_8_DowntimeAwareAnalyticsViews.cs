using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordEventService.Data.Migrations
{
    /// <inheritdoc />
    public partial class P3_8_DowntimeAwareAnalyticsViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW v_observable_window AS
                WITH boundary AS (
                    SELECT COALESCE(
                        MIN(started_at_utc),
                        now()
                    ) AS tracking_start
                    FROM bot_downtime_intervals
                ),
                gaps AS (
                    SELECT
                        COALESCE(
                            LAG(ended_at_utc) OVER (ORDER BY started_at_utc),
                            b.tracking_start
                        ) AS window_start,
                        started_at_utc AS window_end
                    FROM bot_downtime_intervals d, boundary b
                    WHERE d.ended_at_utc IS NOT NULL
                )
                SELECT tstzrange(window_start, window_end, '[)') AS window
                FROM gaps
                WHERE window_start < window_end
                UNION ALL
                SELECT tstzrange(
                    COALESCE(
                        (SELECT MAX(ended_at_utc) FROM bot_downtime_intervals WHERE ended_at_utc IS NOT NULL),
                        (SELECT tracking_start FROM boundary)
                    ),
                    now(),
                    '[)'
                );
                """);

            migrationBuilder.Sql("""
                CREATE VIEW v_voice_sessions AS
                SELECT
                    user_discord_id,
                    guild_discord_id,
                    channel_discord_id_after AS channel_discord_id,
                    event_timestamp_utc AS session_start,
                    COALESCE(
                        LEAD(event_timestamp_utc) OVER (
                            PARTITION BY user_discord_id, guild_discord_id
                            ORDER BY event_timestamp_utc
                        ),
                        now()
                    ) AS session_end
                FROM voice_state_events
                WHERE event_type IN (0, 2)
                  AND channel_discord_id_after IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE VIEW v_voice_session_durations AS
                SELECT
                    s.user_discord_id,
                    s.guild_discord_id,
                    s.channel_discord_id,
                    s.session_start,
                    s.session_end,
                    SUM(
                        EXTRACT(EPOCH FROM (
                            LEAST(s.session_end, upper(w.window))
                            - GREATEST(s.session_start, lower(w.window))
                        ))
                    ) AS observed_seconds
                FROM v_voice_sessions s
                JOIN v_observable_window w
                  ON tstzrange(s.session_start, s.session_end, '[)') && w.window
                GROUP BY s.user_discord_id, s.guild_discord_id, s.channel_discord_id,
                         s.session_start, s.session_end;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW IF EXISTS v_voice_session_durations;
                DROP VIEW IF EXISTS v_voice_sessions;
                DROP VIEW IF EXISTS v_observable_window;
                """);
        }
    }
}
