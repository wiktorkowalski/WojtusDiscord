using DiscordEventService.Data;
using DiscordEventService.Dtos;
using DSharpPlus.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        var eventSpanStart = await db.RawEventLogs.AsNoTracking()
            .OrderBy(e => e.ReceivedAtUtc)
            .Select(e => (DateTime?)e.ReceivedAtUtc)
            .FirstOrDefaultAsync(ct);

        // A correlated subquery picks each non-bot user's most-recent presence row
        // (ReceivedAtUtc desc, Id as a deterministic tiebreak) and projects its three
        // per-device statuses. We MATERIALIZE first, then derive the overall status and
        // filter/sort/take in C#: the device-status enum is not ordered by online-ness
        // (Offline=0, Online=1, Idle=2, DoNotDisturb=4) so the overall status is a
        // priority pick (online > idle > dnd), not a numeric max; and filtering on a
        // correlated subquery in SQL would evaluate it twice and could disagree on ties.
        var candidates = await db.Users.AsNoTracking()
            .Where(u => !u.IsBot)
            .Select(u => new
            {
                u.DiscordId,
                u.Username,
                u.AvatarHash,
                Latest = db.PresenceEvents
                    .Where(p => p.UserDiscordId == u.DiscordId)
                    .OrderByDescending(p => p.ReceivedAtUtc)
                    .ThenByDescending(p => p.Id)
                    .Select(p => new { p.DesktopStatusAfter, p.MobileStatusAfter, p.WebStatusAfter })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var onlineDtos = candidates
            .Where(x => x.Latest is not null)
            .Select(x => new
            {
                x.DiscordId,
                x.Username,
                x.AvatarHash,
                Status = OverallStatus(x.Latest!.DesktopStatusAfter, x.Latest.MobileStatusAfter, x.Latest.WebStatusAfter),
            })
            .Where(x => x.Status != "offline")
            .OrderBy(x => StatusRank(x.Status))
            .ThenBy(x => x.Username)
            .Take(8)
            .Select(x => new GuildOnlineDto(x.DiscordId, x.Username, x.AvatarHash, x.Status))
            .ToList();

        return new GuildDto(
            guild.DiscordId, guild.Name, guild.IconHash,
            memberCount, channelCount, userCount, eventSpanStart, onlineDtos);
    }

    /// <summary>
    /// Maps a single DSharpPlus <see cref="DiscordUserStatus"/> int to its lowercase name.
    /// The enum is NOT ordered by online-ness (Offline=0, Online=1, Idle=2, DoNotDisturb=4,
    /// Invisible=5); Invisible reads as offline.
    /// </summary>
    internal static string StatusName(int status) => status switch
    {
        (int)DiscordUserStatus.Online => "online",
        (int)DiscordUserStatus.Idle => "idle",
        (int)DiscordUserStatus.DoNotDisturb => "dnd",
        _ => "offline",
    };

    /// <summary>
    /// Aggregates a user's three device statuses into one, using Discord's client-status
    /// priority (online &gt; idle &gt; dnd &gt; offline) — not a numeric max, since the
    /// enum values are not ordered by activity.
    /// </summary>
    internal static string OverallStatus(int desktop, int mobile, int web)
    {
        int[] devices = [desktop, mobile, web];
        if (devices.Contains((int)DiscordUserStatus.Online)) return "online";
        if (devices.Contains((int)DiscordUserStatus.Idle)) return "idle";
        if (devices.Contains((int)DiscordUserStatus.DoNotDisturb)) return "dnd";
        return "offline";
    }

    private static int StatusRank(string status) => status switch
    {
        "online" => 0,
        "idle" => 1,
        "dnd" => 2,
        _ => 3,
    };
}
