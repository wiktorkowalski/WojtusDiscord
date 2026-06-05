using DiscordEventService.Data;
using DiscordEventService.Dtos;
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

        // Latest presence per user; overall status = max device after-status
        // (Online=3 > DND=2 > Idle=1 > Offline=0; Math.Max -> GREATEST). Take up to 8
        // non-bot users who are currently non-offline. If there are no presence rows,
        // returns []. A correlated subquery picks each user's most-recent presence row
        // (ordered by ReceivedAtUtc) and projects its overall status.
        var online = await db.Users.AsNoTracking()
            .Where(u => !u.IsBot)
            .Select(u => new
            {
                u.DiscordId,
                u.Username,
                u.AvatarHash,
                Overall = db.PresenceEvents
                    .Where(p => p.UserDiscordId == u.DiscordId)
                    .OrderByDescending(p => p.ReceivedAtUtc)
                    .Select(p => (int?)Math.Max(
                        Math.Max(p.DesktopStatusAfter, p.MobileStatusAfter), p.WebStatusAfter))
                    .FirstOrDefault(),
            })
            .Where(x => x.Overall > 0)
            .OrderByDescending(x => x.Overall)
            .ThenBy(x => x.Username)
            .Take(8)
            .ToListAsync(ct);

        var onlineDtos = online
            .Select(x => new GuildOnlineDto(x.DiscordId, x.Username, x.AvatarHash, StatusName(x.Overall ?? 0)))
            .ToList();

        return new GuildDto(
            guild.DiscordId, guild.Name, guild.IconHash,
            memberCount, channelCount, userCount, eventSpanStart, onlineDtos);
    }

    /// <summary>Maps a Discord presence status int (0..3) to its lowercase name.</summary>
    internal static string StatusName(int overall) => overall switch
    {
        3 => "online",
        2 => "dnd",
        1 => "idle",
        _ => "offline",
    };
}
