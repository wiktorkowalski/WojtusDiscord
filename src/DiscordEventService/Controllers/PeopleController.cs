using System.Data;
using System.Globalization;
using DiscordEventService.Data;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DiscordEventService.Controllers;

/// <summary>
/// Per-person profile: all-time activity totals, a favourite emote, busiest channel,
/// a 14-day message sparkline, current presence status and the name-change history.
/// Keyed by Discord snowflake (route param), 404 when the snowflake is unknown.
/// </summary>
[ApiController]
[Route("api/people")]
public sealed class PeopleController(DiscordDbContext db) : ControllerBase
{
    private const string Tz = "Europe/Warsaw";

    [HttpGet("{discordId:long}/profile")]
    public async Task<ActionResult<ProfileDto>> Profile(long discordId, CancellationToken ct)
    {
        // Snowflakes are stored as unchecked (long)ulong; current ids are < 2^63 so the
        // route's long maps directly to the stored value.
        var user = await db.Users.AsNoTracking()
            .Where(u => u.DiscordId == unchecked((ulong)discordId))
            .Select(u => new
            {
                u.Id,
                u.DiscordId,
                u.Username,
                u.GlobalName,
                u.AvatarHash,
                u.IsBot,
                u.FirstSeenUtc,
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return NotFound();
        }

        var userId = user.Id;
        var discordIdLiteral = discordId.ToString(CultureInfo.InvariantCulture);
        var userIdLiteral = $"'{userId}'";

        var messageCount = await ScalarAsync(
            $"SELECT count(*)::bigint FROM messages WHERE author_id = {userIdLiteral}", ct);

        var memeCount = await ScalarAsync(
            $"SELECT count(*)::bigint FROM messages WHERE author_id = {userIdLiteral} AND (has_attachments OR has_embeds)", ct);

        var reactionsReceived = await ScalarAsync(
            $"""
            SELECT count(*)::bigint
            FROM reaction_events r
            JOIN messages m ON r.message_discord_id = m.discord_id
            WHERE m.author_id = {userIdLiteral} AND r.event_type IN (0, 4)
            """, ct);

        var voiceMinutes = await ScalarAsync(
            $"""
            SELECT COALESCE(round(SUM(EXTRACT(EPOCH FROM (v.next_ts - v.received_at_utc))) / 60.0), 0)::bigint
            FROM (
                SELECT received_at_utc, channel_discord_id_after,
                       LEAD(received_at_utc) OVER (ORDER BY received_at_utc) AS next_ts
                FROM voice_state_events WHERE user_discord_id = {discordIdLiteral}
            ) v
            WHERE v.channel_discord_id_after IS NOT NULL AND v.next_ts IS NOT NULL
            """, ct);

        var onlineMinutes = await ScalarAsync(
            $"""
            SELECT COALESCE(round(SUM(EXTRACT(EPOCH FROM (s.next_ts - s.received_at_utc)) / 60.0)), 0)::bigint
            FROM (
                SELECT received_at_utc,
                       GREATEST(desktop_status_after, mobile_status_after, web_status_after) AS overall,
                       LEAD(received_at_utc) OVER (ORDER BY received_at_utc) AS next_ts
                FROM presence_events WHERE user_discord_id = {discordIdLiteral}
            ) s
            WHERE s.overall > 0 AND s.next_ts IS NOT NULL
              AND s.next_ts - s.received_at_utc <= interval '30 minutes'
              AND NOT EXISTS (
                  SELECT 1 FROM bot_downtime_intervals d
                  WHERE s.received_at_utc < COALESCE(d.ended_at_utc, now())
                    AND s.next_ts > d.started_at_utc
              )
            """, ct);

        var favoriteEmote = await QuerySingleAsync(
            $"""
            SELECT emote_name, emote_discord_id, count(*)::bigint
            FROM reaction_events
            WHERE user_discord_id = {discordIdLiteral} AND event_type IN (0, 4)
            GROUP BY emote_name, emote_discord_id ORDER BY count(*) DESC LIMIT 1
            """,
            r =>
            {
                var emoteId = NullableSnowflake(r, 1);
                return new ProfileFavoriteEmoteDto(r.GetString(0), emoteId, emoteId is > 0);
            }, ct);

        var busiestChannel = await QuerySingleAsync(
            $"""
            SELECT c.name, c.discord_id, count(*)::bigint
            FROM messages m JOIN channels c ON m.channel_id = c.id
            WHERE m.author_id = {userIdLiteral}
            GROUP BY c.id, c.name, c.discord_id ORDER BY count(*) DESC LIMIT 1
            """,
            r => new ProfileBusiestChannelDto(r.GetString(0), Snowflake(r, 1)), ct);

        // Last 14 CET days of the user's messages, zero-filled oldest -> newest.
        var messagesDaily14 = await QueryAsync(
            $"""
            WITH days AS (
                SELECT generate_series(
                    date_trunc('day', now() AT TIME ZONE '{Tz}') - interval '13 days',
                    date_trunc('day', now() AT TIME ZONE '{Tz}'),
                    interval '1 day')::date AS d
            ),
            counts AS (
                SELECT date_trunc('day', created_at_utc AT TIME ZONE '{Tz}')::date AS d, count(*)::bigint AS c
                FROM messages WHERE author_id = {userIdLiteral}
                GROUP BY 1
            )
            SELECT days.d::text, COALESCE(counts.c, 0)::bigint
            FROM days LEFT JOIN counts ON counts.d = days.d
            ORDER BY days.d
            """,
            r => new ProfileDailyPointDto(r.GetString(0), r.GetInt64(1)), ct);

        // Current overall status from the latest presence row.
        var statusInt = await ScalarAsync(
            $"""
            SELECT COALESCE(
                (SELECT GREATEST(desktop_status_after, mobile_status_after, web_status_after)
                 FROM presence_events WHERE user_discord_id = {discordIdLiteral}
                 ORDER BY received_at_utc DESC LIMIT 1), 0)::bigint
            """, ct);

        var nameHistory = await db.UserNameHistory.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.ChangedAtUtc)
            .Select(h => new NameChangeDto(
                h.UsernameBefore, h.UsernameAfter, h.GlobalNameBefore, h.GlobalNameAfter, h.ChangedAtUtc))
            .ToListAsync(ct);

        return new ProfileDto(
            user.DiscordId, user.Username, user.GlobalName, user.AvatarHash, user.IsBot,
            GuildController.StatusName((int)statusInt), user.FirstSeenUtc,
            messageCount, memeCount, reactionsReceived, voiceMinutes, onlineMinutes,
            favoriteEmote, busiestChannel, messagesDaily14, nameHistory);
    }

    // ---- helpers (mirror StatsController's raw-SQL pattern) ----

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

    private static ulong Snowflake(NpgsqlDataReader r, int i) => unchecked((ulong)r.GetInt64(i));

    private static ulong? NullableSnowflake(NpgsqlDataReader r, int i) =>
        r.IsDBNull(i) ? null : unchecked((ulong)r.GetInt64(i));
}
