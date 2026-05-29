using System.Data;
using DiscordEventService.Data;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DiscordEventService.Controllers;

/// <summary>
/// Pre-aggregated statistics across four buckets: Volume &amp; trends, People, Places,
/// Behavior. Time buckets use guild-local time (Europe/Warsaw) computed server-side.
/// Queries are fixed (no client identifiers) raw SQL over base tables — no dependency
/// on the prod-only SQL views, so the same queries run under Testcontainers.
/// </summary>
[ApiController]
[Route("api/stats")]
public sealed class StatsController(DiscordDbContext db) : ControllerBase
{
    private const string Tz = "Europe/Warsaw";

    // Calendar "today" boundary in guild-local time, as a UTC instant.
    private const string TodayStart =
        $"(date_trunc('day', now() AT TIME ZONE '{Tz}') AT TIME ZONE '{Tz}')";

    // ---- Overview (home dashboard) ----

    [HttpGet("overview")]
    public async Task<OverviewDto> Overview(CancellationToken ct)
    {
        var messages = await QuerySingleAsync(
            $"""
            SELECT count(*) FILTER (WHERE created_at_utc >= {TodayStart})::bigint,
                   count(*) FILTER (WHERE created_at_utc >= now() - interval '7 days')::bigint,
                   count(*) FILTER (WHERE created_at_utc >= now() - interval '30 days')::bigint,
                   count(*)::bigint
            FROM messages
            """,
            r => new WindowCountsDto(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)), ct)
            ?? new WindowCountsDto(0, 0, 0, 0);

        var reactions = await QuerySingleAsync(
            $"""
            SELECT count(*) FILTER (WHERE received_at_utc >= {TodayStart})::bigint,
                   count(*) FILTER (WHERE received_at_utc >= now() - interval '7 days')::bigint,
                   count(*) FILTER (WHERE received_at_utc >= now() - interval '30 days')::bigint,
                   count(*)::bigint
            FROM reaction_events WHERE event_type = 0
            """,
            r => new WindowCountsDto(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)), ct)
            ?? new WindowCountsDto(0, 0, 0, 0);

        var counts = await QuerySingleAsync(
            """
            SELECT (SELECT count(*) FROM users)::bigint,
                   (SELECT count(*) FROM channels WHERE NOT is_deleted)::bigint,
                   (SELECT count(*) FROM raw_event_logs)::bigint
            """,
            r => (Users: r.GetInt64(0), Channels: r.GetInt64(1), Events: r.GetInt64(2)), ct);

        var voiceMinutes = await QuerySingleAsync(
            """
            SELECT COALESCE(round(SUM(EXTRACT(EPOCH FROM (next_ts - received_at_utc))) / 60.0), 0)::bigint
            FROM (
                SELECT received_at_utc, channel_discord_id_after,
                       LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
                FROM voice_state_events
            ) v
            WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
            """,
            r => r.GetInt64(0), ct);

        var topChatter = await QuerySingleAsync(
            """
            SELECT u.username, u.discord_id, count(*)::bigint
            FROM messages m JOIN users u ON m.author_id = u.id
            GROUP BY u.id, u.username, u.discord_id ORDER BY count(*) DESC LIMIT 1
            """,
            r => new UserStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2)), ct);

        var topChannel = await QuerySingleAsync(
            """
            SELECT c.name, c.discord_id, count(m.id)::bigint AS msgs,
                   (SELECT count(*) FROM reaction_events r
                    WHERE r.channel_discord_id = c.discord_id AND r.event_type = 0)::bigint
            FROM channels c LEFT JOIN messages m ON m.channel_id = c.id
            GROUP BY c.id, c.name, c.discord_id ORDER BY msgs DESC LIMIT 1
            """,
            r => new ChannelActivityDto(r.GetString(0), Snowflake(r, 1), r.GetInt64(2), r.GetInt64(3)), ct);

        var messagesDaily = await QueryAsync(
            $"""
            SELECT date_trunc('day', created_at_utc AT TIME ZONE '{Tz}')::date::text, count(*)::bigint
            FROM messages WHERE created_at_utc >= now() - interval '30 days'
            GROUP BY 1 ORDER BY 1
            """,
            r => new DailyPointDto(r.GetString(0), r.GetInt64(1)), ct);

        var topEmojis = await QueryAsync(
            """
            SELECT emote_name, emote_discord_id, count(*)::bigint
            FROM reaction_events WHERE event_type = 0
            GROUP BY emote_name, emote_discord_id ORDER BY count(*) DESC LIMIT 5
            """,
            r =>
            {
                var emoteId = NullableSnowflake(r, 1);
                return new EmojiStatDto(r.GetString(0), emoteId, emoteId is > 0, r.GetInt64(2));
            }, ct);

        return new OverviewDto(
            messages.Total, reactions.Total, counts.Events, voiceMinutes,
            counts.Users, counts.Channels, messages, reactions,
            topChatter, topChannel, messagesDaily, topEmojis);
    }

    // ---- Volume & trends ----

    [HttpGet("volume/daily")]
    public Task<List<VolumeDailyDto>> VolumeDaily(CancellationToken ct) => QueryAsync(
        $"""
        SELECT date_trunc('day', received_at_utc AT TIME ZONE '{Tz}')::date::text AS day, count(*)::bigint
        FROM raw_event_logs WHERE NOT serialization_failed
        GROUP BY day ORDER BY day
        """,
        r => new VolumeDailyDto(r.GetString(0), r.GetInt64(1)), ct);

    [HttpGet("volume/by-type")]
    public Task<List<VolumeByTypeDto>> VolumeByType(CancellationToken ct) => QueryAsync(
        """
        SELECT event_type, count(*)::bigint FROM raw_event_logs WHERE NOT serialization_failed
        GROUP BY event_type ORDER BY count(*) DESC LIMIT 15
        """,
        r => new VolumeByTypeDto(r.GetString(0), r.GetInt64(1)), ct);

    [HttpGet("volume/hourly")]
    public Task<List<VolumeHourlyDto>> VolumeHourly(CancellationToken ct) => QueryAsync(
        $"""
        SELECT EXTRACT(HOUR FROM received_at_utc AT TIME ZONE '{Tz}')::int AS hour, count(*)::bigint
        FROM raw_event_logs WHERE NOT serialization_failed
        GROUP BY hour ORDER BY hour
        """,
        r => new VolumeHourlyDto(r.GetInt32(0), r.GetInt64(1)), ct);

    // ---- People ----

    [HttpGet("people/top-messages")]
    public Task<List<UserStatDto>> TopMessages(CancellationToken ct) => QueryAsync(
        """
        SELECT u.username, u.discord_id, count(*)::bigint
        FROM messages m JOIN users u ON m.author_id = u.id
        GROUP BY u.id, u.username, u.discord_id ORDER BY count(*) DESC LIMIT 20
        """,
        r => new UserStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2)), ct);

    [HttpGet("people/top-reactions-given")]
    public Task<List<UserStatDto>> TopReactionsGiven(CancellationToken ct) => QueryAsync(
        """
        SELECT u.username, r.user_discord_id, count(*)::bigint
        FROM reaction_events r LEFT JOIN users u ON u.discord_id = r.user_discord_id
        WHERE r.event_type = 0
        GROUP BY u.username, r.user_discord_id ORDER BY count(*) DESC LIMIT 20
        """,
        r => new UserStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2)), ct);

    [HttpGet("people/top-reactions-received")]
    public Task<List<UserStatDto>> TopReactionsReceived(CancellationToken ct) => QueryAsync(
        """
        SELECT u.username, u.discord_id, count(*)::bigint
        FROM reaction_events r
        JOIN messages m ON r.message_discord_id = m.discord_id
        JOIN users u ON m.author_id = u.id
        WHERE r.event_type = 0
        GROUP BY u.id, u.username, u.discord_id ORDER BY count(*) DESC LIMIT 20
        """,
        r => new UserStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2)), ct);

    /// <summary>
    /// Voice minutes per user. Each event where the user is in a channel
    /// (channel_discord_id_after not null) contributes the gap until their next
    /// event. Open sessions (no following event) are excluded — surfaced in the UI.
    /// </summary>
    [HttpGet("people/voice-leaderboard")]
    public Task<List<VoiceStatDto>> VoiceLeaderboard(CancellationToken ct) => QueryAsync(
        """
        SELECT u.username, v.user_discord_id,
               round(SUM(EXTRACT(EPOCH FROM (v.next_ts - v.received_at_utc))) / 60.0)::bigint AS minutes
        FROM (
            SELECT user_discord_id, received_at_utc, channel_discord_id_after,
                   LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
            FROM voice_state_events
        ) v
        LEFT JOIN users u ON u.discord_id = v.user_discord_id
        WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
        GROUP BY u.username, v.user_discord_id ORDER BY minutes DESC LIMIT 20
        """,
        r => new VoiceStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2)), ct);

    // ---- Places ----

    [HttpGet("places/channel-activity")]
    public Task<List<ChannelActivityDto>> ChannelActivity(CancellationToken ct) => QueryAsync(
        """
        SELECT c.name, c.discord_id, count(m.id)::bigint AS msgs,
               (SELECT count(*) FROM reaction_events r
                WHERE r.channel_discord_id = c.discord_id AND r.event_type = 0)::bigint AS reactions
        FROM channels c LEFT JOIN messages m ON m.channel_id = c.id
        GROUP BY c.id, c.name, c.discord_id ORDER BY msgs DESC LIMIT 50
        """,
        r => new ChannelActivityDto(r.GetString(0), Snowflake(r, 1), r.GetInt64(2), r.GetInt64(3)), ct);

    // ---- Behavior ----

    [HttpGet("behavior/top-emojis")]
    public Task<List<EmojiStatDto>> TopEmojis(CancellationToken ct) => QueryAsync(
        """
        SELECT emote_name, emote_discord_id, count(*)::bigint
        FROM reaction_events WHERE event_type = 0
        GROUP BY emote_name, emote_discord_id ORDER BY count(*) DESC LIMIT 25
        """,
        r =>
        {
            var emoteId = NullableSnowflake(r, 1);
            return new EmojiStatDto(r.GetString(0), emoteId, emoteId is > 0, r.GetInt64(2));
        }, ct);

    /// <summary>
    /// Top presence activities ("playing X"). NOTE: includes presence artifacts such
    /// as "Custom Status" and "Playing N/10" — surfaced (not hidden) with a UI note.
    /// </summary>
    [HttpGet("behavior/top-activities")]
    public Task<List<ActivityStatDto>> TopActivities(CancellationToken ct) => QueryAsync(
        """
        SELECT name, count(*)::bigint FROM activities WHERE name IS NOT NULL
        GROUP BY name ORDER BY count(*) DESC LIMIT 25
        """,
        r => new ActivityStatDto(r.GetString(0), r.GetInt64(1)), ct);

    [HttpGet("behavior/heatmap")]
    public Task<List<HeatmapCellDto>> Heatmap(CancellationToken ct) => QueryAsync(
        $"""
        SELECT EXTRACT(DOW FROM created_at_utc AT TIME ZONE '{Tz}')::int AS dow,
               EXTRACT(HOUR FROM created_at_utc AT TIME ZONE '{Tz}')::int AS hour,
               count(*)::bigint
        FROM messages GROUP BY dow, hour
        """,
        r => new HeatmapCellDto(r.GetInt32(0), r.GetInt32(1), r.GetInt64(2)), ct);

    // ---- helpers ----

    private async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<T>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(map(reader));
        }
        return results;
    }

    private async Task<T?> QuerySingleAsync<T>(string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? map(reader) : default;
    }

    private static string? NullableString(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static ulong Snowflake(NpgsqlDataReader r, int i) => unchecked((ulong)r.GetInt64(i));

    private static ulong? NullableSnowflake(NpgsqlDataReader r, int i) =>
        r.IsDBNull(i) ? null : unchecked((ulong)r.GetInt64(i));
}
