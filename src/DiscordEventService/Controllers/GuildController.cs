using DiscordEventService.Data;
using DiscordEventService.Dtos;
using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Controllers;

// Counts are global (single-guild deployment); the guild identity row is chosen
// deterministically (still-joined row first, then earliest first-seen) so it is stable
// across requests.
[ApiController]
[Route("api/guild")]
public sealed class GuildController(DiscordDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GuildDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GuildDto>> Get(CancellationToken ct)
    {
        var guild = await db.Guilds.AsNoTracking()
            .OrderBy(g => g.LeftAtUtc == null ? 0 : 1)
            .ThenBy(g => g.FirstSeenUtc)
            .Select(g => new { g.DiscordId, g.Name, g.IconHash })
            .FirstOrDefaultAsync(ct);

        if (guild is null)
            return NotFound();

        var memberCount = await db.Members.LongCountAsync(ct);
        var channelCount = await db.Channels.LongCountAsync(c => !c.IsDeleted, ct);
        var userCount = await db.Users.LongCountAsync(ct);
        var eventSpanStart = await db.RawEventLogs.AsNoTracking()
            .OrderBy(e => e.ReceivedAtUtc)
            .Select(e => (DateTime?)e.ReceivedAtUtc)
            .FirstOrDefaultAsync(ct);

        var onlineDtos = await GetOnlineUsersAsync(ct);

        return new GuildDto(
            guild.DiscordId, guild.Name, guild.IconHash,
            memberCount, channelCount, userCount, eventSpanStart, onlineDtos);
    }

    // A correlated subquery picks each non-bot user's most-recent presence row
    // (ReceivedAtUtc desc, Id as a deterministic tiebreak) and projects its three
    // per-device statuses. We MATERIALIZE first, then derive the overall status
    // (PresenceStatus.Overall — a priority pick, not a numeric max) and
    // filter/sort/take in C#: filtering on a correlated subquery in SQL would
    // evaluate it twice and could disagree on ties.
    private async Task<List<GuildOnlineDto>> GetOnlineUsersAsync(CancellationToken ct)
    {
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

        return candidates
            .Where(x => x.Latest is not null)
            .Select(x => new
            {
                x.DiscordId,
                x.Username,
                x.AvatarHash,
                Status = PresenceStatus.Overall(x.Latest!.DesktopStatusAfter, x.Latest.MobileStatusAfter, x.Latest.WebStatusAfter),
            })
            .Where(x => x.Status != PresenceStatus.Offline)
            .OrderBy(x => PresenceStatus.Rank(x.Status))
            .ThenBy(x => x.Username)
            .Take(8)
            .Select(x => new GuildOnlineDto(x.DiscordId, x.Username, x.AvatarHash, x.Status))
            .ToList();
    }

}
