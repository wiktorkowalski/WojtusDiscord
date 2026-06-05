using System.Data;
using System.Globalization;
using System.Text.Json;
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

        // Reactions that are "present": Added (0) live + Backfilled (4) historical. Removed
        // (1) / Cleared (2) / EmojiCleared (3) are excluded. Backfill imported ~26k of these,
        // so filtering to event_type=0 alone would under-count reactions ~30x.
        var reactions = await QuerySingleAsync(
            $"""
            SELECT count(*) FILTER (WHERE received_at_utc >= {TodayStart})::bigint,
                   count(*) FILTER (WHERE received_at_utc >= now() - interval '7 days')::bigint,
                   count(*) FILTER (WHERE received_at_utc >= now() - interval '30 days')::bigint,
                   count(*)::bigint
            FROM reaction_events WHERE event_type IN (0, 4)
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
            SELECT u.username, u.discord_id, count(*)::bigint, u.avatar_hash
            FROM messages m JOIN users u ON m.author_id = u.id
            GROUP BY u.id, u.username, u.discord_id, u.avatar_hash ORDER BY count(*) DESC LIMIT 1
            """,
            r => new UserStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2), NullableString(r, 3)), ct);

        var topChannel = await QuerySingleAsync(
            """
            SELECT c.name, c.discord_id, count(m.id)::bigint AS msgs,
                   (SELECT count(*) FROM reaction_events r
                    WHERE r.channel_discord_id = c.discord_id AND r.event_type IN (0, 4))::bigint
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
            FROM reaction_events WHERE event_type IN (0, 4)
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
        SELECT u.username, u.discord_id, count(*)::bigint, u.avatar_hash
        FROM messages m JOIN users u ON m.author_id = u.id
        GROUP BY u.id, u.username, u.discord_id, u.avatar_hash ORDER BY count(*) DESC LIMIT 20
        """,
        r => new UserStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2), NullableString(r, 3)), ct);

    [HttpGet("people/top-reactions-given")]
    public Task<List<UserStatDto>> TopReactionsGiven(CancellationToken ct) => QueryAsync(
        """
        SELECT u.username, r.user_discord_id, count(*)::bigint, u.avatar_hash
        FROM reaction_events r LEFT JOIN users u ON u.discord_id = r.user_discord_id
        WHERE r.event_type IN (0, 4)
        GROUP BY u.username, r.user_discord_id, u.avatar_hash ORDER BY count(*) DESC LIMIT 20
        """,
        r => new UserStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2), NullableString(r, 3)), ct);

    [HttpGet("people/top-reactions-received")]
    public Task<List<UserStatDto>> TopReactionsReceived(CancellationToken ct) => QueryAsync(
        """
        SELECT u.username, u.discord_id, count(*)::bigint, u.avatar_hash
        FROM reaction_events r
        JOIN messages m ON r.message_discord_id = m.discord_id
        JOIN users u ON m.author_id = u.id
        WHERE r.event_type IN (0, 4)
        GROUP BY u.id, u.username, u.discord_id, u.avatar_hash ORDER BY count(*) DESC LIMIT 20
        """,
        r => new UserStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2), NullableString(r, 3)), ct);

    /// <summary>
    /// Voice minutes per user. Each event where the user is in a channel
    /// (channel_discord_id_after not null) contributes the gap until their next
    /// event. Open sessions (no following event) are excluded — surfaced in the UI.
    /// </summary>
    [HttpGet("people/voice-leaderboard")]
    public Task<List<VoiceStatDto>> VoiceLeaderboard(CancellationToken ct) => QueryAsync(
        """
        SELECT u.username, v.user_discord_id,
               round(SUM(EXTRACT(EPOCH FROM (v.next_ts - v.received_at_utc))) / 60.0)::bigint AS minutes,
               u.avatar_hash
        FROM (
            SELECT user_discord_id, received_at_utc, channel_discord_id_after,
                   LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
            FROM voice_state_events
        ) v
        LEFT JOIN users u ON u.discord_id = v.user_discord_id
        WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
        GROUP BY u.username, v.user_discord_id, u.avatar_hash ORDER BY minutes DESC LIMIT 20
        """,
        r => new VoiceStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2), NullableString(r, 3)), ct);

    // ---- Places ----

    [HttpGet("places/channel-activity")]
    public Task<List<ChannelActivityDto>> ChannelActivity(CancellationToken ct) => QueryAsync(
        """
        SELECT c.name, c.discord_id, count(m.id)::bigint AS msgs,
               (SELECT count(*) FROM reaction_events r
                WHERE r.channel_discord_id = c.discord_id AND r.event_type IN (0, 4))::bigint AS reactions
        FROM channels c LEFT JOIN messages m ON m.channel_id = c.id
        GROUP BY c.id, c.name, c.discord_id ORDER BY msgs DESC LIMIT 50
        """,
        r => new ChannelActivityDto(r.GetString(0), Snowflake(r, 1), r.GetInt64(2), r.GetInt64(3)), ct);

    // ---- Behavior ----

    [HttpGet("behavior/top-emojis")]
    public Task<List<EmojiStatDto>> TopEmojis(CancellationToken ct) => QueryAsync(
        """
        SELECT emote_name, emote_discord_id, count(*)::bigint
        FROM reaction_events WHERE event_type IN (0, 4)
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

    // ---- Community (windowed headline metrics + leaderboards) ----

    /// <summary>
    /// Headline community metrics over a rolling window (week=7d, month=30d, all=since
    /// launch) with a same-length preceding-window comparison (null for <c>all</c>) and
    /// a dense daily sparkline. Leaderboards are the top 6 per metric in the window.
    /// </summary>
    [HttpGet("community")]
    public async Task<ActionResult<CommunityDto>> Community(
        [FromQuery] string range = "week", CancellationToken ct = default)
    {
        range = (range ?? "week").Trim().ToLowerInvariant();
        if (range is not ("week" or "month" or "all"))
        {
            return BadRequest(new { error = "range must be one of: week, month, all." });
        }

        var (curStart, curEnd, prevStart, prevEnd, sparkDays, label, prevLabel) =
            await ResolveWindowAsync(range, ct);

        // Current/previous window SQL fragments reused across metric queries.
        var curWindow = $"BETWEEN {Sql(curStart)} AND {Sql(curEnd)}";
        string? prevWindow = prevStart is null || prevEnd is null
            ? null
            : $"BETWEEN {Sql(prevStart.Value)} AND {Sql(prevEnd.Value)}";

        var messages = await MetricAsync(
            $"SELECT count(*)::bigint FROM messages WHERE created_at_utc {{0}}",
            "messages", "created_at_utc", curStart, curEnd, prevWindow, sparkDays, ct);

        var memes = await MetricAsync(
            $"SELECT count(*)::bigint FROM messages WHERE (has_attachments OR has_embeds) AND created_at_utc {{0}}",
            "messages", "created_at_utc", curStart, curEnd, prevWindow, sparkDays, ct,
            sparkFilter: "(has_attachments OR has_embeds)");

        var reactions = await MetricAsync(
            "SELECT count(*)::bigint FROM reaction_events WHERE event_type IN (0, 4) AND received_at_utc {0}",
            "reaction_events", "received_at_utc", curStart, curEnd, prevWindow, sparkDays, ct,
            sparkFilter: "event_type IN (0, 4)");

        var activeMembers = await MetricAsync(
            "SELECT count(DISTINCT author_id)::bigint FROM messages WHERE created_at_utc {0}",
            "messages", "created_at_utc", curStart, curEnd, prevWindow, sparkDays, ct,
            sparkAggregate: "count(DISTINCT author_id)");

        var voiceMinutes = await VoiceMinutesMetricAsync(curStart, curEnd, prevStart, prevEnd, sparkDays, ct);
        var onlineMinutes = await OnlineMinutesMetricAsync(curStart, curEnd, prevStart, prevEnd, sparkDays, ct);

        var leaderboards = new CommunityLeaderboardsDto(
            await LeaderAsync(
                $"""
                SELECT u.username, u.discord_id, u.avatar_hash, count(*)::bigint
                FROM messages m JOIN users u ON m.author_id = u.id
                WHERE m.created_at_utc {curWindow}
                GROUP BY u.id, u.username, u.discord_id, u.avatar_hash ORDER BY 4 DESC LIMIT 6
                """, ct),
            await LeaderAsync(
                $"""
                SELECT u.username, u.discord_id, u.avatar_hash, count(*)::bigint
                FROM messages m JOIN users u ON m.author_id = u.id
                WHERE (m.has_attachments OR m.has_embeds) AND m.created_at_utc {curWindow}
                GROUP BY u.id, u.username, u.discord_id, u.avatar_hash ORDER BY 4 DESC LIMIT 6
                """, ct),
            await LeaderAsync(
                $"""
                SELECT u.username, u.discord_id, u.avatar_hash, count(*)::bigint
                FROM reaction_events r
                JOIN messages m ON r.message_discord_id = m.discord_id
                JOIN users u ON m.author_id = u.id
                WHERE r.event_type IN (0, 4) AND r.received_at_utc {curWindow}
                GROUP BY u.id, u.username, u.discord_id, u.avatar_hash ORDER BY 4 DESC LIMIT 6
                """, ct),
            await LeaderAsync(
                $"""
                SELECT u.username, v.user_discord_id, u.avatar_hash,
                       round(SUM(EXTRACT(EPOCH FROM (v.next_ts - v.received_at_utc))) / 60.0)::bigint
                FROM (
                    SELECT user_discord_id, received_at_utc, channel_discord_id_after,
                           LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
                    FROM voice_state_events
                ) v
                LEFT JOIN users u ON u.discord_id = v.user_discord_id
                WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
                  AND v.received_at_utc {curWindow}
                GROUP BY u.username, v.user_discord_id, u.avatar_hash ORDER BY 4 DESC LIMIT 6
                """, ct));

        var metrics = new CommunityMetricsDto(
            messages, memes, reactions, voiceMinutes, onlineMinutes, activeMembers);

        return new CommunityDto(range, label, prevLabel, metrics, leaderboards);
    }

    // ---- Spotify ----

    /// <summary>
    /// Spotify listening: who is listening right now (active Listening activities) and
    /// the most-played tracks all-time. Artist is the first entry of the JSON artist
    /// array stored on the activity.
    /// NOTE (data reality): the bot currently records only the activity name ("Spotify")
    /// for Listening presence — the rich track fields (song title, artist, album, art) are
    /// not captured and are NULL/placeholder in stored data. So <c>track</c> is null when
    /// the stored "song title" is just the generic activity name, and top-tracks excludes
    /// that placeholder (yielding an empty list until richer presence is ingested). The
    /// now-playing tile still honestly shows who is currently listening.
    /// </summary>
    [HttpGet("spotify")]
    public async Task<SpotifyDto> Spotify(CancellationToken ct)
    {
        var nowPlaying = await QueryAsync(
            """
            SELECT u.discord_id, u.username, u.avatar_hash,
                   NULLIF(a.spotify_song_title, a.name),
                   a.spotify_artists_json, a.spotify_album_title, a.spotify_album_art_url,
                   a.spotify_track_start_utc, a.spotify_track_end_utc
            FROM activities a JOIN users u ON a.user_id = u.id
            WHERE a.activity_type = 2 AND a.is_active = true
            ORDER BY a.last_seen_at_utc DESC LIMIT 5
            """,
            r => new SpotifyNowPlayingDto(
                Snowflake(r, 0), NullableString(r, 1), NullableString(r, 2), NullableString(r, 3),
                FirstArtist(NullableString(r, 4)), NullableString(r, 5), NullableString(r, 6),
                NullableDateTime(r, 7), NullableDateTime(r, 8)),
            ct);

        var topTracks = await QueryAsync(
            """
            SELECT spotify_song_title,
                   (array_agg(spotify_artists_json) FILTER (WHERE spotify_artists_json IS NOT NULL))[1],
                   (array_agg(spotify_album_art_url) FILTER (WHERE spotify_album_art_url IS NOT NULL))[1],
                   count(*)::bigint
            FROM activities
            WHERE spotify_song_title IS NOT NULL AND spotify_song_title IS DISTINCT FROM name
            GROUP BY spotify_song_title ORDER BY count(*) DESC LIMIT 5
            """,
            r => new SpotifyTrackDto(
                r.GetString(0), FirstArtist(NullableString(r, 1)), NullableString(r, 2), r.GetInt64(3)),
            ct);

        return new SpotifyDto(nowPlaying, topTracks);
    }

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

    private static DateTime? NullableDateTime(NpgsqlDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);

    // ---- Community window helpers ----

    // Window resolution: anchor everything to a single DB now() so the current and
    // preceding windows line up exactly (the spark series, prev comparison and counts
    // all share the same boundaries). "launch" = earliest raw event.
    private async Task<(DateTime CurStart, DateTime CurEnd, DateTime? PrevStart, DateTime? PrevEnd,
        int SparkDays, string Label, string PrevLabel)> ResolveWindowAsync(string range, CancellationToken ct)
    {
        var (now, launch) = await QuerySingleAsync(
            "SELECT now() AT TIME ZONE 'UTC', (SELECT min(received_at_utc) FROM raw_event_logs)",
            r => (Now: DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc),
                  Launch: r.IsDBNull(1) ? (DateTime?)null : DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc)),
            ct);

        switch (range)
        {
            case "week":
                return (now.AddDays(-7), now, now.AddDays(-14), now.AddDays(-7), 7,
                    "Last 7 days", "Previous 7 days");
            case "month":
                return (now.AddDays(-30), now, now.AddDays(-60), now.AddDays(-30), 30,
                    "Last 30 days", "Previous 30 days");
            default: // all
                var start = launch ?? now;
                var days = Math.Max(1, (int)Math.Ceiling((now - start).TotalDays) + 1);
                return (start, now, null, null, days, "All time", string.Empty);
        }
    }

    // Builds value/prev/dense-spark for a count-style metric. <paramref name="countSql"/>
    // has a single {0} placeholder for the window predicate; spark is computed with a
    // generate_series of CET day buckets LEFT JOINed to per-day counts so every day is
    // present (zero-filled), oldest -> newest.
    private async Task<CommunityMetricDto> MetricAsync(
        string countSql, string table, string tsColumn, DateTime curStart, DateTime curEnd,
        string? prevWindow, int sparkDays, CancellationToken ct,
        string? sparkFilter = null, string sparkAggregate = "count(*)")
    {
        var value = await ScalarAsync(
            string.Format(CultureInfo.InvariantCulture, countSql, $"BETWEEN {Sql(curStart)} AND {Sql(curEnd)}"), ct);

        long? prev = prevWindow is null
            ? null
            : await ScalarAsync(string.Format(CultureInfo.InvariantCulture, countSql, prevWindow), ct);

        var spark = await SparkAsync(table, tsColumn, sparkFilter, sparkAggregate, sparkDays, ct);
        return new CommunityMetricDto(value, prev, spark);
    }

    // Dense daily series: the last <paramref name="sparkDays"/> CET days ending today,
    // zero-filled, oldest -> newest. Anchored on now() AT TIME ZONE so the series and the
    // per-day counts resolve in the same wall-clock day (windows end at now()).
    private async Task<IReadOnlyList<long>> SparkAsync(
        string table, string tsColumn, string? filter, string aggregate, int sparkDays, CancellationToken ct)
    {
        var where = filter is null ? string.Empty : $"WHERE {filter}";
        var sql =
            $"""
            WITH days AS (
                SELECT generate_series(
                    date_trunc('day', now() AT TIME ZONE '{Tz}') - interval '{sparkDays - 1} days',
                    date_trunc('day', now() AT TIME ZONE '{Tz}'),
                    interval '1 day')::date AS d
            ),
            counts AS (
                SELECT date_trunc('day', {tsColumn} AT TIME ZONE '{Tz}')::date AS d, {aggregate}::bigint AS c
                FROM {table} {where}
                GROUP BY 1
            )
            SELECT COALESCE(counts.c, 0)::bigint
            FROM days LEFT JOIN counts ON counts.d = days.d
            ORDER BY days.d
            """;
        return await QueryAsync(sql, r => r.GetInt64(0), ct);
    }

    // Voice minutes: sessionize via LEAD() and count a segment when its start is inside
    // the window. Spark buckets segment minutes by the CET day of the segment start.
    private async Task<CommunityMetricDto> VoiceMinutesMetricAsync(
        DateTime curStart, DateTime curEnd, DateTime? prevStart, DateTime? prevEnd, int sparkDays, CancellationToken ct)
    {
        const string sessions =
            """
            SELECT user_discord_id, received_at_utc, channel_discord_id_after,
                   LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
            FROM voice_state_events
            """;

        async Task<long> Sum(DateTime start, DateTime end) => await ScalarAsync(
            $"""
            SELECT COALESCE(round(SUM(EXTRACT(EPOCH FROM (v.next_ts - v.received_at_utc))) / 60.0), 0)::bigint
            FROM ({sessions}) v
            WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
              AND v.received_at_utc BETWEEN {Sql(start)} AND {Sql(end)}
            """, ct);

        var value = await Sum(curStart, curEnd);
        long? prev = prevStart is null || prevEnd is null ? null : await Sum(prevStart.Value, prevEnd.Value);

        var spark = await VoiceSparkAsync(sessions, sparkDays, ct);
        return new CommunityMetricDto(value, prev, spark);
    }

    private async Task<IReadOnlyList<long>> VoiceSparkAsync(
        string sessions, int sparkDays, CancellationToken ct)
    {
        var sql =
            $"""
            WITH days AS (
                SELECT generate_series(
                    date_trunc('day', now() AT TIME ZONE '{Tz}') - interval '{sparkDays - 1} days',
                    date_trunc('day', now() AT TIME ZONE '{Tz}'),
                    interval '1 day')::date AS d
            ),
            seg AS (
                SELECT date_trunc('day', v.received_at_utc AT TIME ZONE '{Tz}')::date AS d,
                       SUM(EXTRACT(EPOCH FROM (v.next_ts - v.received_at_utc)) / 60.0) AS m
                FROM ({sessions}) v
                WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
                GROUP BY 1
            )
            SELECT COALESCE(round(seg.m), 0)::bigint
            FROM days LEFT JOIN seg ON seg.d = days.d
            ORDER BY days.d
            """;
        return await QueryAsync(sql, r => r.GetInt64(0), ct);
    }

    /// <summary>
    /// Online minutes (best-effort approximation). Discord does not expose presence
    /// durations, so we sessionize presence_events per user: each event's contribution
    /// is the gap until the next event, counted only when (a) the event's overall device
    /// status is non-offline (max device after-status &gt; 0), (b) the gap is at most 30
    /// minutes — a cap that keeps stale/offline tails and bot-downtime windows from
    /// inflating the figure (segments with a longer gap are dropped entirely, not
    /// clamped), and (c) the segment does not overlap a recorded bot-downtime interval.
    /// Segment minutes are attributed to the window/day of the segment start. Treat the
    /// result as an approximation, not a precise online-time ledger.
    /// </summary>
    private async Task<CommunityMetricDto> OnlineMinutesMetricAsync(
        DateTime curStart, DateTime curEnd, DateTime? prevStart, DateTime? prevEnd, int sparkDays, CancellationToken ct)
    {
        // segments = per-user presence events with the gap to the next event and a flag
        // for non-offline status; eligible = gap <= 30 min, status non-offline, and no
        // overlap with any bot_downtime_interval (open intervals coalesced to now()).
        const string segments =
            """
            SELECT s.user_discord_id, s.received_at_utc AS seg_start, s.next_ts AS seg_end,
                   EXTRACT(EPOCH FROM (s.next_ts - s.received_at_utc)) / 60.0 AS minutes
            FROM (
                SELECT user_discord_id, received_at_utc,
                       GREATEST(desktop_status_after, mobile_status_after, web_status_after) AS overall,
                       LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
                FROM presence_events
            ) s
            WHERE s.overall > 0 AND s.next_ts IS NOT NULL
              AND s.next_ts - s.received_at_utc <= interval '30 minutes'
              AND NOT EXISTS (
                  SELECT 1 FROM bot_downtime_intervals d
                  WHERE s.received_at_utc < COALESCE(d.ended_at_utc, now())
                    AND s.next_ts > d.started_at_utc
              )
            """;

        async Task<long> Sum(DateTime start, DateTime end) => await ScalarAsync(
            $"""
            SELECT COALESCE(round(SUM(minutes)), 0)::bigint
            FROM ({segments}) seg
            WHERE seg.seg_start BETWEEN {Sql(start)} AND {Sql(end)}
            """, ct);

        var value = await Sum(curStart, curEnd);
        long? prev = prevStart is null || prevEnd is null ? null : await Sum(prevStart.Value, prevEnd.Value);

        var spark = await OnlineSparkAsync(segments, sparkDays, ct);
        return new CommunityMetricDto(value, prev, spark);
    }

    private async Task<IReadOnlyList<long>> OnlineSparkAsync(
        string segments, int sparkDays, CancellationToken ct)
    {
        var sql =
            $"""
            WITH days AS (
                SELECT generate_series(
                    date_trunc('day', now() AT TIME ZONE '{Tz}') - interval '{sparkDays - 1} days',
                    date_trunc('day', now() AT TIME ZONE '{Tz}'),
                    interval '1 day')::date AS d
            ),
            seg AS (
                SELECT date_trunc('day', s.seg_start AT TIME ZONE '{Tz}')::date AS d, SUM(s.minutes) AS m
                FROM ({segments}) s
                GROUP BY 1
            )
            SELECT COALESCE(round(seg.m), 0)::bigint
            FROM days LEFT JOIN seg ON seg.d = days.d
            ORDER BY days.d
            """;
        return await QueryAsync(sql, r => r.GetInt64(0), ct);
    }

    private Task<List<CommunityLeaderEntryDto>> LeaderAsync(string sql, CancellationToken ct) => QueryAsync(
        sql,
        r => new CommunityLeaderEntryDto(NullableString(r, 0), Snowflake(r, 1), NullableString(r, 2), r.GetInt64(3)),
        ct);

    private async Task<long> ScalarAsync(string sql, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long l ? l : 0L;
    }

    // Explicit-UTC TIMESTAMPTZ literal for safe interpolation into timestamp comparisons.
    // The trailing +00 pins the offset so the comparison is correct regardless of the
    // connection's session TimeZone (the column is timestamptz).
    private static string Sql(DateTime dt) =>
        $"TIMESTAMPTZ '{dt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}+00'";

    // First element of a stored JSON artist array (e.g. ["A","B"]); null-safe.
    private static string? FirstArtist(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                return first.ValueKind == JsonValueKind.String ? first.GetString() : null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
