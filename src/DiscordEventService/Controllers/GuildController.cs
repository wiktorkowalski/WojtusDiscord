using System.Data;
using DiscordEventService.Data;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DiscordEventService.Controllers;

/// <summary>
/// Server header: identity, headline counts, and a small "who's online now" list
/// derived from the latest presence row per user. Counts are global (single-guild
/// deployment); the guild identity row is chosen deterministically (still-joined row
/// first, then earliest first-seen) so it is stable across requests.
/// </summary>
[ApiController]
[Route("api/guild")]
public sealed class GuildController(DiscordDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<GuildDto>> Get(CancellationToken ct)
    {
        var guild = await db.Guilds.AsNoTracking()
            .OrderBy(g => g.LeftAtUtc == null ? 0 : 1)
            .ThenBy(g => g.FirstSeenUtc)
            .Select(g => new { g.DiscordId, g.Name, g.IconHash })
            .FirstOrDefaultAsync(ct);

        if (guild is null)
        {
            return NotFound();
        }

        var memberCount = await db.Members.LongCountAsync(ct);
        var channelCount = await db.Channels.LongCountAsync(c => !c.IsDeleted, ct);
        var userCount = await db.Users.LongCountAsync(ct);
        var eventSpanStart = await db.RawEventLogs
            .OrderBy(e => e.ReceivedAtUtc)
            .Select(e => (DateTime?)e.ReceivedAtUtc)
            .FirstOrDefaultAsync(ct);

        // Latest presence per user; overall status = max device after-status
        // (Online=3 > DND=2 > Idle=1 > Offline=0). Take up to 8 non-bot users who
        // are currently non-offline. If there are no presence rows, returns [].
        var online = await QueryAsync(
            """
            SELECT p.user_discord_id, u.username, u.avatar_hash, p.overall
            FROM (
                SELECT DISTINCT ON (user_discord_id) user_discord_id,
                       GREATEST(desktop_status_after, mobile_status_after, web_status_after) AS overall
                FROM presence_events
                ORDER BY user_discord_id, received_at_utc DESC
            ) p
            JOIN users u ON u.discord_id = p.user_discord_id
            WHERE p.overall > 0 AND NOT u.is_bot
            ORDER BY p.overall DESC, u.username
            LIMIT 8
            """,
            r => new GuildOnlineDto(
                Snowflake(r, 0), NullableString(r, 1), NullableString(r, 2), StatusName(r.GetInt32(3))),
            ct);

        return new GuildDto(
            guild.DiscordId, guild.Name, guild.IconHash,
            memberCount, channelCount, userCount, eventSpanStart, online);
    }

    /// <summary>Maps a Discord presence status int (0..3) to its lowercase name.</summary>
    internal static string StatusName(int overall) => overall switch
    {
        3 => "online",
        2 => "dnd",
        1 => "idle",
        _ => "offline",
    };

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

    private static string? NullableString(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static ulong Snowflake(NpgsqlDataReader r, int i) => unchecked((ulong)r.GetInt64(i));
}
