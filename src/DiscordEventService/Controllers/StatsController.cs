using System.Data;
using System.Globalization;
using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Dtos;
using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DiscordEventService.Controllers;

[ApiController]
[Route("api/stats")]
public sealed class StatsController(DiscordDbContext db) : ControllerBase
{
    private const string Tz = "Europe/Warsaw";

    // Calendar "today" boundary in guild-local time, as a UTC instant.
    private const string TodayStart =
        $"(date_trunc('day', now() AT TIME ZONE '{Tz}') AT TIME ZONE '{Tz}')";

    // "Present" reactions in raw SQL: Added (0) live + Backfilled (4) historical (Removed/
    // Cleared/EmojiCleared excluded; filtering to 0 alone under-counts ~30x). The raw-SQL
    // mirror of ReactionEventQueryExtensions.WherePresent(); prefix with a table alias where
    // the query uses one, e.g. $"... r.{ReactionPresentSql}".
    private const string ReactionPresentSql = "event_type IN (0, 4)";

    // List-size tuning knobs for the dashboard endpoints.
    private const int OverviewTopEmojis = 5;
    private const int TopEventTypesLimit = 15;
    private const int LeaderboardSize = 20;
    private const int BehaviorListSize = 25;
    private const int CommunityLeaderboardSize = 6;
    private const int ChannelActivityLimit = 50;
    private const int SpotifyListSize = 5;

    // Shared query bodies: the home Overview tile and the dedicated Stats endpoint run
    // the same aggregation with a different LIMIT, so each is defined once (append the
    // limit at the call site) to keep the two in sync.
    // Per-channel message + present-reaction counts. Reactions are pre-aggregated in a
    // single grouped subquery and LEFT JOINed (one row per channel) rather than a
    // correlated per-row count(*), which scanned reaction_events once PER channel.
    // Stays raw: the equivalent EF LINQ is either a correlated per-channel count (the N+1
    // this fixes) or a multi-GroupJoin that reads worse than this single grouped join.
    private const string ChannelActivitySql =
        $"""
        SELECT c.name, c.discord_id, count(m.id)::bigint AS msgs,
               COALESCE(rc.reactions, 0)::bigint AS reactions
        FROM channels c
        LEFT JOIN messages m ON m.channel_id = c.id
        LEFT JOIN (
            SELECT channel_discord_id, count(*)::bigint AS reactions
            FROM reaction_events WHERE {ReactionPresentSql}
            GROUP BY channel_discord_id
        ) rc ON rc.channel_discord_id = c.discord_id
        GROUP BY c.id, c.name, c.discord_id, rc.reactions ORDER BY msgs DESC, c.id LIMIT
        """;

    // Voice minutes credited for one session segment [v.received_at_utc, v.next_ts),
    // MINUS any overlap with a bot_downtime_intervals window (open intervals coalesced
    // to now()). During an outage a "leave" can go unobserved, so the raw gap to the
    // user's next event would otherwise count the whole blind window as voice time (the
    // 2026-05 blackout would credit ~14k phantom minutes to anyone left in a channel).
    // We SUBTRACT the downtime overlap rather than drop the segment, so a legitimate
    // multi-hour call spanning a brief restart is preserved. Every voice query aliases
    // the sessionized rows as `v`. Mirrors the downtime handling in OnlineMinutesMetricAsync.
    private const string VoiceSegmentMinutes =
        """
        GREATEST(0, EXTRACT(EPOCH FROM (v.next_ts - v.received_at_utc)) - COALESCE((
            SELECT SUM(EXTRACT(EPOCH FROM (
                LEAST(v.next_ts, COALESCE(d.ended_at_utc, now()))
              - GREATEST(v.received_at_utc, d.started_at_utc))))
            FROM bot_downtime_intervals d
            WHERE v.received_at_utc < COALESCE(d.ended_at_utc, now())
              AND v.next_ts > d.started_at_utc), 0)) / 60.0
        """;

    [HttpGet("overview")]
    [ProducesResponseType<OverviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OverviewDto>> Overview(CancellationToken ct)
    {
        var messages = await GetMessageWindowCountsAsync(ct);
        var reactions = await GetReactionWindowCountsAsync(ct);

        var counts = await QuerySingleAsync(
            """
            SELECT (SELECT count(*) FROM users)::bigint,
                   (SELECT count(*) FROM channels WHERE NOT is_deleted)::bigint,
                   (SELECT count(*) FROM raw_event_logs)::bigint
            """,
            r => (Users: r.GetInt64(0), Channels: r.GetInt64(1), Events: r.GetInt64(2)), ct);

        var voiceMinutes = await GetTotalVoiceMinutesAsync(ct);

        var topChatter = await TopChatters().FirstOrDefaultAsync(ct);

        var topChannel = await QuerySingleAsync(
            $"{ChannelActivitySql} 1",
            r => new ChannelActivityDto(r.GetString(0), Snowflake(r, 1), r.GetInt64(2), r.GetInt64(3)), ct);

        var messagesDaily = await GetMessagesDaily30Async(ct);

        var topEmojis = await TopEmojisAsync(OverviewTopEmojis, ct);

        return new OverviewDto(
            messages.Total, reactions.Total, counts.Events, voiceMinutes,
            counts.Users, counts.Channels, messages, reactions,
            topChatter, topChannel, messagesDaily, topEmojis);
    }

    // volume/daily + volume/hourly stay raw: they bucket by CET calendar day / hour via
    // `... AT TIME ZONE 'Europe/Warsaw'`, which Npgsql's EF Core provider does not translate
    // from LINQ. volume/by-type (no time bucketing) is plain EF below.

    [HttpGet("volume/daily")]
    [ProducesResponseType<List<VolumeDailyDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<VolumeDailyDto>>> VolumeDaily(CancellationToken ct) => await QueryAsync(
        $"""
        SELECT date_trunc('day', received_at_utc AT TIME ZONE '{Tz}')::date::text AS day, count(*)::bigint
        FROM raw_event_logs WHERE NOT serialization_failed
        GROUP BY day ORDER BY day
        """,
        r => new VolumeDailyDto(r.GetString(0), r.GetInt64(1)), ct);

    [HttpGet("volume/by-type")]
    [ProducesResponseType<List<VolumeByTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<VolumeByTypeDto>>> VolumeByType(CancellationToken ct) => await
        db.RawEventLogs.AsNoTracking()
            .Where(e => !e.IsSerializationFailed)
            .GroupBy(e => e.EventType)
            .Select(g => new { EventType = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.EventType)
            .Take(TopEventTypesLimit)
            .Select(x => new VolumeByTypeDto(x.EventType, x.Count))
            .ToListAsync(ct);

    [HttpGet("volume/hourly")]
    [ProducesResponseType<List<VolumeHourlyDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<VolumeHourlyDto>>> VolumeHourly(CancellationToken ct) => await QueryAsync(
        $"""
        SELECT EXTRACT(HOUR FROM received_at_utc AT TIME ZONE '{Tz}')::int AS hour, count(*)::bigint
        FROM raw_event_logs WHERE NOT serialization_failed
        GROUP BY hour ORDER BY hour
        """,
        r => new VolumeHourlyDto(r.GetInt32(0), r.GetInt64(1)), ct);

    [HttpGet("people/top-messages")]
    [ProducesResponseType<List<UserStatDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserStatDto>>> TopMessages(CancellationToken ct) => await
        TopChatters().Take(LeaderboardSize).ToListAsync(ct);

    // Reactions GIVEN per user. LEFT-join semantics (a reactor with no stored user row keeps
    // a null username) are preserved by resolving the user via a correlated lookup on the
    // top 20 — bounded, index-backed, no whole-table join.
    [HttpGet("people/top-reactions-given")]
    [ProducesResponseType<List<UserStatDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserStatDto>>> TopReactionsGiven(CancellationToken ct) => await
        db.ReactionEvents.AsNoTracking().WherePresent()
            .GroupBy(r => r.UserDiscordId)
            .Select(g => new { UserDiscordId = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.UserDiscordId)
            .Take(LeaderboardSize)
            .Select(x => new UserStatDto(
                db.Users.Where(u => u.DiscordId == x.UserDiscordId).Select(u => u.Username).FirstOrDefault(),
                x.UserDiscordId,
                x.Count,
                db.Users.Where(u => u.DiscordId == x.UserDiscordId).Select(u => u.AvatarHash).FirstOrDefault()))
            .ToListAsync(ct);

    // Reactions RECEIVED per author (reaction -> its message -> the message author). Inner
    // joins: only reactions on stored messages with a known author count.
    [HttpGet("people/top-reactions-received")]
    [ProducesResponseType<List<UserStatDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserStatDto>>> TopReactionsReceived(CancellationToken ct) => await
        db.ReactionEvents.AsNoTracking().WherePresent()
            .Join(db.Messages.AsNoTracking(), r => r.MessageDiscordId, m => m.DiscordId, (r, m) => m.Author)
            .GroupBy(a => new { a.Username, a.DiscordId, a.AvatarHash })
            .Select(g => new { g.Key.Username, g.Key.DiscordId, g.Key.AvatarHash, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.DiscordId)
            .Take(LeaderboardSize)
            .Select(x => new UserStatDto(x.Username, x.DiscordId, x.Count, x.AvatarHash))
            .ToListAsync(ct);

    // Open sessions (no following event) are excluded — surfaced in the UI.
    [HttpGet("people/voice-leaderboard")]
    [ProducesResponseType<List<VoiceStatDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<VoiceStatDto>>> VoiceLeaderboard(CancellationToken ct) => await QueryAsync(
        $"""
        SELECT u.username, v.user_discord_id,
               round(SUM({VoiceSegmentMinutes}))::bigint AS minutes,
               u.avatar_hash
        FROM (
            SELECT user_discord_id, received_at_utc, channel_discord_id_after,
                   LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
            FROM voice_state_events
        ) v
        LEFT JOIN users u ON u.discord_id = v.user_discord_id
        WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
        GROUP BY u.username, v.user_discord_id, u.avatar_hash ORDER BY minutes DESC, v.user_discord_id LIMIT {LeaderboardSize}
        """,
        r => new VoiceStatDto(NullableString(r, 0), Snowflake(r, 1), r.GetInt64(2), NullableString(r, 3)), ct);

    [HttpGet("places/channel-activity")]
    [ProducesResponseType<List<ChannelActivityDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ChannelActivityDto>>> ChannelActivity(CancellationToken ct) => await QueryAsync(
        $"{ChannelActivitySql} {ChannelActivityLimit}",
        r => new ChannelActivityDto(r.GetString(0), Snowflake(r, 1), r.GetInt64(2), r.GetInt64(3)), ct);

    [HttpGet("behavior/top-emojis")]
    [ProducesResponseType<List<EmojiStatDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EmojiStatDto>>> TopEmojis(CancellationToken ct) => await TopEmojisAsync(BehaviorListSize, ct);

    // Includes presence artifacts such as "Custom Status" and "Playing N/10" —
    // surfaced (not hidden) with a UI note.
    [HttpGet("behavior/top-activities")]
    [ProducesResponseType<List<ActivityStatDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ActivityStatDto>>> TopActivities(CancellationToken ct) => await
        db.Activities.AsNoTracking()
            .Where(a => a.Name != null)
            .GroupBy(a => a.Name)
            .Select(g => new { Name = g.Key!, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .Take(BehaviorListSize)
            .Select(x => new ActivityStatDto(x.Name, x.Count))
            .ToListAsync(ct);

    // Stays raw: buckets by CET day-of-week + hour via `AT TIME ZONE 'Europe/Warsaw'`, which
    // the Npgsql EF Core provider does not translate from LINQ.
    [HttpGet("behavior/heatmap")]
    [ProducesResponseType<List<HeatmapCellDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<HeatmapCellDto>>> Heatmap(CancellationToken ct) => await QueryAsync(
        $"""
        SELECT EXTRACT(DOW FROM created_at_utc AT TIME ZONE '{Tz}')::int AS dow,
               EXTRACT(HOUR FROM created_at_utc AT TIME ZONE '{Tz}')::int AS hour,
               count(*)::bigint
        FROM messages GROUP BY dow, hour
        """,
        r => new HeatmapCellDto(r.GetInt32(0), r.GetInt32(1), r.GetInt64(2)), ct);

    [HttpGet("community")]
    [ProducesResponseType<CommunityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CommunityDto>> Community(
        [FromQuery] string range = "week", CancellationToken ct = default)
    {
        range = (range ?? "week").Trim().ToLowerInvariant();
        if (range is not ("week" or "month" or "all"))
            return BadRequest(new { error = "range must be one of: week, month, all." });

        var (curStart, curEnd, prevStart, prevEnd, sparkDays, label, prevLabel) =
            await ResolveWindowAsync(range, ct);

        var metrics = await BuildCommunityMetricsAsync(curStart, curEnd, prevStart, prevEnd, sparkDays, ct);
        var leaderboards = await BuildCommunityLeaderboardsAsync(curStart, curEnd, ct);

        return new CommunityDto(range, label, prevLabel, metrics, leaderboards);
    }

    // Data reality: the bot currently records only the activity name ("Spotify") for
    // Listening presence — the rich track fields (title, artist, album, art) are
    // NULL/placeholder. So track is null when the stored "song title" is just the generic
    // activity name, and top-tracks excludes that placeholder until richer presence is
    // ingested.
    [HttpGet("spotify")]
    [ProducesResponseType<SpotifyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SpotifyDto>> Spotify(CancellationToken ct)
    {
        var nowPlayingRows = await db.Activities.AsNoTracking()
            .Where(a => a.ActivityType == 2 && a.IsActive)
            .OrderByDescending(a => a.LastSeenAtUtc)
            .ThenBy(a => a.Id)
            .Take(SpotifyListSize)
            .Select(a => new
            {
                a.User.DiscordId,
                a.User.Username,
                a.User.AvatarHash,
                a.SpotifySongTitle,
                a.Name,
                a.SpotifyArtistsJson,
                a.SpotifyAlbumTitle,
                a.SpotifyAlbumArtUrl,
                a.SpotifyTrackStartUtc,
                a.SpotifyTrackEndUtc,
            })
            .ToListAsync(ct);

        var nowPlaying = nowPlayingRows
            .Select(a => new SpotifyNowPlayingDto(
                a.DiscordId, a.Username, a.AvatarHash,
                a.SpotifySongTitle == a.Name ? null : a.SpotifySongTitle,
                FirstArtist(a.SpotifyArtistsJson), a.SpotifyAlbumTitle, a.SpotifyAlbumArtUrl,
                AsUtc(a.SpotifyTrackStartUtc), AsUtc(a.SpotifyTrackEndUtc)))
            .ToList();

        // Most-played tracks all-time. Excludes the placeholder where the stored "song
        // title" is just the generic activity name. Representative artist/art = first
        // non-null in the group (matches the prior array_agg(... FILTER ...)[1]).
        var topTrackRows = await db.Activities.AsNoTracking()
            .Where(a => a.SpotifySongTitle != null && a.SpotifySongTitle != a.Name)
            .GroupBy(a => a.SpotifySongTitle)
            .Select(g => new
            {
                Track = g.Key!,
                ArtistsJson = g.Select(a => a.SpotifyArtistsJson).FirstOrDefault(a => a != null),
                AlbumArtUrl = g.Select(a => a.SpotifyAlbumArtUrl).FirstOrDefault(a => a != null),
                Plays = g.LongCount(),
            })
            .OrderByDescending(x => x.Plays)
            .ThenBy(x => x.Track)
            .Take(SpotifyListSize)
            .ToListAsync(ct);

        var topTracks = topTrackRows
            .Select(x => new SpotifyTrackDto(x.Track, FirstArtist(x.ArtistsJson), x.AlbumArtUrl, x.Plays))
            .ToList();

        return new SpotifyDto(nowPlaying, topTracks);
    }

    // Top message authors, ordered by count. Shared by the Overview tile (top 1) and the
    // People endpoint (top 20); they differ only in row count. Same GROUP BY as the
    // Community topChatters leaderboard.
    private IQueryable<UserStatDto> TopChatters() =>
        db.Messages.AsNoTracking()
            .GroupBy(m => new { m.Author.Username, m.Author.DiscordId, m.Author.AvatarHash })
            .Select(g => new { g.Key.Username, g.Key.DiscordId, g.Key.AvatarHash, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.DiscordId)
            .Select(x => new UserStatDto(x.Username, x.DiscordId, x.Count, x.AvatarHash));

    // Most-used reaction emotes (Overview top 5 / Behavior top 25). IsCustom (custom emotes
    // carry a snowflake id) is computed in memory after the grouped count.
    private async Task<List<EmojiStatDto>> TopEmojisAsync(int limit, CancellationToken ct)
    {
        var rows = await db.ReactionEvents.AsNoTracking().WherePresent()
            .GroupBy(r => new { r.EmoteName, r.EmoteDiscordId })
            .Select(g => new { g.Key.EmoteName, g.Key.EmoteDiscordId, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.EmoteName)
            .ThenBy(x => x.EmoteDiscordId)
            .Take(limit)
            .ToListAsync(ct);
        return rows
            .Select(x => new EmojiStatDto(x.EmoteName, x.EmoteDiscordId, x.EmoteDiscordId is > 0, x.Count))
            .ToList();
    }

    // ---- Count metrics: value/prev via LINQ; dense daily sparks stay raw (see SparkAsync). ----
    private async Task<CommunityMetricsDto> BuildCommunityMetricsAsync(
        DateTime curStart, DateTime curEnd, DateTime? prevStart, DateTime? prevEnd, int sparkDays, CancellationToken ct)
    {
        var hasPrev = prevStart is not null && prevEnd is not null;


        var messages = new CommunityMetricDto(
            await db.Messages.AsNoTracking()
                .LongCountAsync(m => m.CreatedAtUtc >= curStart && m.CreatedAtUtc <= curEnd, ct),
            !hasPrev ? null : await db.Messages.AsNoTracking()
                .LongCountAsync(m => m.CreatedAtUtc >= prevStart!.Value && m.CreatedAtUtc <= prevEnd!.Value, ct),
            await SparkAsync("messages", "created_at_utc", null, "count(*)", sparkDays, ct));

        var memes = new CommunityMetricDto(
            await db.Messages.AsNoTracking()
                .LongCountAsync(m => (m.HasAttachments || m.HasEmbeds)
                    && m.CreatedAtUtc >= curStart && m.CreatedAtUtc <= curEnd, ct),
            !hasPrev ? null : await db.Messages.AsNoTracking()
                .LongCountAsync(m => (m.HasAttachments || m.HasEmbeds)
                    && m.CreatedAtUtc >= prevStart!.Value && m.CreatedAtUtc <= prevEnd!.Value, ct),
            await SparkAsync("messages", "created_at_utc", "(has_attachments OR has_embeds)", "count(*)", sparkDays, ct));

        var reactions = new CommunityMetricDto(
            await db.ReactionEvents.AsNoTracking().WherePresent()
                .LongCountAsync(r => r.ReceivedAtUtc >= curStart && r.ReceivedAtUtc <= curEnd, ct),
            !hasPrev ? null : await db.ReactionEvents.AsNoTracking().WherePresent()
                .LongCountAsync(r => r.ReceivedAtUtc >= prevStart!.Value && r.ReceivedAtUtc <= prevEnd!.Value, ct),
            await SparkAsync("reaction_events", "received_at_utc", ReactionPresentSql, "count(*)", sparkDays, ct));

        var activeMembers = new CommunityMetricDto(
            await db.Messages.AsNoTracking()
                .Where(m => m.CreatedAtUtc >= curStart && m.CreatedAtUtc <= curEnd)
                .Select(m => m.AuthorId).Distinct().LongCountAsync(ct),
            !hasPrev ? null : await db.Messages.AsNoTracking()
                .Where(m => m.CreatedAtUtc >= prevStart!.Value && m.CreatedAtUtc <= prevEnd!.Value)
                .Select(m => m.AuthorId).Distinct().LongCountAsync(ct),
            await SparkAsync("messages", "created_at_utc", null, "count(DISTINCT author_id)", sparkDays, ct));

        var voiceMinutes = await VoiceMinutesMetricAsync(curStart, curEnd, prevStart, prevEnd, sparkDays, ct);
        var onlineMinutes = await OnlineMinutesMetricAsync(curStart, curEnd, prevStart, prevEnd, sparkDays, ct);

        return new CommunityMetricsDto(messages, memes, reactions, voiceMinutes, onlineMinutes, activeMembers);
    }

    // ---- Leaderboards: chatters/memes/reactions via LINQ; voice stays raw (LEAD sessionization). ----
    private async Task<CommunityLeaderboardsDto> BuildCommunityLeaderboardsAsync(
        DateTime curStart, DateTime curEnd, CancellationToken ct)
    {
        var topChatters = await db.Messages.AsNoTracking()
            .Where(m => m.CreatedAtUtc >= curStart && m.CreatedAtUtc <= curEnd)
            .GroupBy(m => new { m.Author.Username, m.Author.DiscordId, m.Author.AvatarHash })
            .Select(g => new { g.Key.Username, g.Key.DiscordId, g.Key.AvatarHash, Value = g.LongCount() })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.DiscordId)
            .Take(CommunityLeaderboardSize)
            .Select(x => new CommunityLeaderEntryDto(x.Username, x.DiscordId, x.AvatarHash, x.Value))
            .ToListAsync(ct);

        var memeLords = await db.Messages.AsNoTracking()
            .Where(m => (m.HasAttachments || m.HasEmbeds) && m.CreatedAtUtc >= curStart && m.CreatedAtUtc <= curEnd)
            .GroupBy(m => new { m.Author.Username, m.Author.DiscordId, m.Author.AvatarHash })
            .Select(g => new { g.Key.Username, g.Key.DiscordId, g.Key.AvatarHash, Value = g.LongCount() })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.DiscordId)
            .Take(CommunityLeaderboardSize)
            .Select(x => new CommunityLeaderEntryDto(x.Username, x.DiscordId, x.AvatarHash, x.Value))
            .ToListAsync(ct);

        var reactionsReceived = await db.ReactionEvents.AsNoTracking().WherePresent()
            .Where(r => r.ReceivedAtUtc >= curStart && r.ReceivedAtUtc <= curEnd)
            .Join(
                db.Messages.AsNoTracking(),
                r => r.MessageDiscordId,
                m => m.DiscordId,
                (r, m) => m.Author)
            .GroupBy(a => new { a.Username, a.DiscordId, a.AvatarHash })
            .Select(g => new { g.Key.Username, g.Key.DiscordId, g.Key.AvatarHash, Value = g.LongCount() })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.DiscordId)
            .Take(CommunityLeaderboardSize)
            .Select(x => new CommunityLeaderEntryDto(x.Username, x.DiscordId, x.AvatarHash, x.Value))
            .ToListAsync(ct);

        var voice = await LeaderAsync(
            $"""
            SELECT u.username, v.user_discord_id, u.avatar_hash,
                   round(SUM({VoiceSegmentMinutes}))::bigint
            FROM (
                SELECT user_discord_id, received_at_utc, channel_discord_id_after,
                       LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
                FROM voice_state_events
            ) v
            LEFT JOIN users u ON u.discord_id = v.user_discord_id
            WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
              AND v.received_at_utc BETWEEN {Sql(curStart)} AND {Sql(curEnd)}
            GROUP BY u.username, v.user_discord_id, u.avatar_hash ORDER BY 4 DESC, v.user_discord_id LIMIT {CommunityLeaderboardSize}
            """, ct);

        return new CommunityLeaderboardsDto(topChatters, memeLords, reactionsReceived, voice);
    }

    private async Task<WindowCountsDto> GetMessageWindowCountsAsync(CancellationToken ct) =>
        await QuerySingleAsync(
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
    private async Task<WindowCountsDto> GetReactionWindowCountsAsync(CancellationToken ct) =>
        await QuerySingleAsync(
            $"""
            SELECT count(*) FILTER (WHERE received_at_utc >= {TodayStart})::bigint,
                   count(*) FILTER (WHERE received_at_utc >= now() - interval '7 days')::bigint,
                   count(*) FILTER (WHERE received_at_utc >= now() - interval '30 days')::bigint,
                   count(*)::bigint
            FROM reaction_events WHERE {ReactionPresentSql}
            """,
            r => new WindowCountsDto(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)), ct)
            ?? new WindowCountsDto(0, 0, 0, 0);

    private Task<long> GetTotalVoiceMinutesAsync(CancellationToken ct) =>
        QuerySingleAsync(
            $"""
            SELECT COALESCE(round(SUM({VoiceSegmentMinutes})), 0)::bigint
            FROM (
                SELECT received_at_utc, channel_discord_id_after,
                       LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
                FROM voice_state_events
            ) v
            WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
            """,
            r => r.GetInt64(0), ct);

    // Dense 30-CET-day series (today + prior 29), zero-filled. The cutoff is the
    // UTC instant of the earliest CET day's midnight (not now()-30d), so the first
    // bucket isn't understated by the UTC/CET offset; missing days render as 0 so the
    // index-positioned area chart isn't compressed.
    private Task<List<DailyPointDto>> GetMessagesDaily30Async(CancellationToken ct) =>
        QueryAsync(
            $"""
            WITH {DaysCte(30)},
            counts AS (
                SELECT date_trunc('day', created_at_utc AT TIME ZONE '{Tz}')::date AS d, count(*)::bigint AS c
                FROM messages
                WHERE created_at_utc >= ((date_trunc('day', now() AT TIME ZONE '{Tz}') - interval '29 days') AT TIME ZONE '{Tz}')
                GROUP BY 1
            )
            SELECT days.d::text, COALESCE(counts.c, 0)::bigint
            FROM days LEFT JOIN counts ON counts.d = days.d
            ORDER BY days.d
            """,
            r => new DailyPointDto(r.GetString(0), r.GetInt64(1)), ct);

    private async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<T>();
        while (await reader.ReadAsync(ct))
            results.Add(map(reader));
        return results;
    }

    private async Task<T?> QuerySingleAsync<T>(string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? map(reader) : default;
    }

    private static string? NullableString(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static ulong Snowflake(NpgsqlDataReader r, int i) => unchecked((ulong)r.GetInt64(i));

    // Pin the kind to UTC so the serialized timestamp carries an explicit offset,
    // regardless of how Npgsql materializes the column kind.
    private static DateTime? AsUtc(DateTime? dt) =>
        dt is null ? null : DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);

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
                // Count CET calendar days inclusive (launch-day..today). A UTC-instant
                // Ceiling()+1 could prepend a spurious leading zero-day depending on the
                // time-of-day, misaligning the spark from the generate_series buckets.
                var tz = TimeZoneInfo.FindSystemTimeZoneById(Tz);
                var launchDay = TimeZoneInfo.ConvertTimeFromUtc(start, tz).Date;
                var nowDay = TimeZoneInfo.ConvertTimeFromUtc(now, tz).Date;
                var days = Math.Max(1, (nowDay - launchDay).Days + 1);
                return (start, now, null, null, days, "All time", string.Empty);
        }
    }

    // Shared CTE body: one row per day for the last `sparkDays` CET calendar days ending
    // today (oldest -> newest). Compose as `WITH {DaysCte(n)}, <more CTEs> SELECT ...`.
    private static string DaysCte(int sparkDays) =>
        $"""
        days AS (
            SELECT generate_series(
                date_trunc('day', now() AT TIME ZONE '{Tz}') - interval '{sparkDays - 1} days',
                date_trunc('day', now() AT TIME ZONE '{Tz}'),
                interval '1 day')::date AS d
        )
        """;

    // Dense daily series: the last <paramref name="sparkDays"/> CET days ending today,
    // zero-filled, oldest -> newest. Anchored on now() AT TIME ZONE so the series and the
    // per-day counts resolve in the same wall-clock day (windows end at now()).
    // Stays raw: dense CET-day series needs generate_series + date_trunc AT TIME ZONE,
    // which has no clean LINQ equivalent (and zero-filling client-side would re-bucket).
    private async Task<IReadOnlyList<long>> SparkAsync(
        string table, string tsColumn, string? filter, string aggregate, int sparkDays, CancellationToken ct)
    {
        var where = filter is null ? string.Empty : $"WHERE {filter}";
        var sql =
            $"""
            WITH {DaysCte(sparkDays)},
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
    // Stays raw: whole-guild LEAD() window sessionization — a LINQ version would pull all
    // voice_state_events into memory.
    private async Task<CommunityMetricDto> VoiceMinutesMetricAsync(
        DateTime curStart, DateTime curEnd, DateTime? prevStart, DateTime? prevEnd, int sparkDays, CancellationToken ct)
    {
        const string sessions =
            """
            SELECT user_discord_id, received_at_utc, channel_discord_id_after,
                   LEAD(received_at_utc) OVER (PARTITION BY user_discord_id ORDER BY received_at_utc) AS next_ts
            FROM voice_state_events
            """;

        async Task<long> SumAsync(DateTime start, DateTime end) => await ScalarAsync(
            $"""
            SELECT COALESCE(round(SUM({VoiceSegmentMinutes})), 0)::bigint
            FROM ({sessions}) v
            WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
              AND v.received_at_utc BETWEEN {Sql(start)} AND {Sql(end)}
            """, ct);

        var value = await SumAsync(curStart, curEnd);
        long? prev = prevStart is null || prevEnd is null ? null : await SumAsync(prevStart.Value, prevEnd.Value);

        var spark = await VoiceSparkAsync(sessions, sparkDays, ct);
        return new CommunityMetricDto(value, prev, spark);
    }

    private async Task<IReadOnlyList<long>> VoiceSparkAsync(
        string sessions, int sparkDays, CancellationToken ct)
    {
        var sql =
            $"""
            WITH {DaysCte(sparkDays)},
            seg AS (
                SELECT date_trunc('day', v.received_at_utc AT TIME ZONE '{Tz}')::date AS d,
                       SUM({VoiceSegmentMinutes}) AS m
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
    /// Stays raw: whole-guild LEAD() window sessionization (over ~100k presence rows) — a
    /// LINQ version would pull every presence row into memory.
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

        async Task<long> SumAsync(DateTime start, DateTime end) => await ScalarAsync(
            $"""
            SELECT COALESCE(round(SUM(minutes)), 0)::bigint
            FROM ({segments}) seg
            WHERE seg.seg_start BETWEEN {Sql(start)} AND {Sql(end)}
            """, ct);

        var value = await SumAsync(curStart, curEnd);
        long? prev = prevStart is null || prevEnd is null ? null : await SumAsync(prevStart.Value, prevEnd.Value);

        var spark = await OnlineSparkAsync(segments, sparkDays, ct);
        return new CommunityMetricDto(value, prev, spark);
    }

    private async Task<IReadOnlyList<long>> OnlineSparkAsync(
        string segments, int sparkDays, CancellationToken ct)
    {
        var sql =
            $"""
            WITH {DaysCte(sparkDays)},
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
            await connection.OpenAsync(ct);

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
            return null;

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
