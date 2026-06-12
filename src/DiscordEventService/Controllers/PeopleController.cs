using System.Globalization;
using DiscordEventService.Data;
using DiscordEventService.Dtos;
using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Controllers;

[ApiController]
[Route("api/people")]
public sealed class PeopleController(DiscordDbContext db) : ControllerBase
{
    private const string Tz = "Europe/Warsaw";

    // Daily-activity sparkline window on the profile.
    private const int SparklineDays = 14;

    // Presence sessions: gaps longer than this break an online streak.
    private static readonly TimeSpan MaxPresenceGap = TimeSpan.FromMinutes(30);

    [HttpGet("{discordId:long}/profile")]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileDto>> Profile(long discordId, CancellationToken ct)
    {
        // Snowflakes are stored as unchecked (long)ulong; current ids are < 2^63 so the
        // route's long maps directly to the stored value.
        var discordSf = unchecked((ulong)discordId);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.DiscordId == discordSf)
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
            return NotFound();

        var userId = user.Id;

        var messageCount = await db.Messages.AsNoTracking()
            .LongCountAsync(m => m.AuthorId == userId, ct);

        var memeCount = await db.Messages.AsNoTracking()
            .LongCountAsync(m => m.AuthorId == userId && (m.HasAttachments || m.HasEmbeds), ct);

        var reactionsReceived = await CountReactionsReceivedAsync(userId, ct);

        var voiceMinutes = await VoiceMinutesAsync(discordSf, ct);
        var onlineMinutes = await OnlineMinutesAsync(discordSf, ct);

        var favoriteEmoteDto = await GetFavoriteEmoteAsync(discordSf, ct);
        var busiestChannelDto = await GetBusiestChannelAsync(userId, ct);

        var messagesDaily14 = await MessagesDaily14Async(userId, ct);

        var status = await GetCurrentStatusAsync(discordSf, ct);

        var nameHistory = await db.UserNameHistory.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.ChangedAtUtc)
            .Select(h => new NameChangeDto(
                h.UsernameBefore, h.UsernameAfter, h.GlobalNameBefore, h.GlobalNameAfter, h.ChangedAtUtc))
            .ToListAsync(ct);

        return new ProfileDto(
            user.DiscordId, user.Username, user.GlobalName, user.AvatarHash, user.IsBot,
            status, user.FirstSeenUtc,
            messageCount, memeCount, reactionsReceived, voiceMinutes, onlineMinutes,
            favoriteEmoteDto, busiestChannelDto, messagesDaily14, nameHistory);
    }

    // Voice minutes: each event where the user is in a channel
    // (ChannelDiscordIdAfter not null) contributes the gap until their next event.
    // Per-user rows are small, so sessionize in memory.
    private async Task<long> VoiceMinutesAsync(ulong discordSf, CancellationToken ct)
    {
        var events = await db.VoiceStateEvents.AsNoTracking()
            .Where(v => v.UserDiscordId == discordSf)
            .OrderBy(v => v.ReceivedAtUtc)
            .Select(v => new { v.ReceivedAtUtc, v.ChannelDiscordIdAfter })
            .ToListAsync(ct);

        var downtimes = await db.BotDowntimeIntervals.AsNoTracking()
            .Select(d => new { d.StartedAtUtc, d.EndedAtUtc })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var totalMinutes = 0d;
        for (var i = 0; i < events.Count - 1; i++)
        {
            if (events[i].ChannelDiscordIdAfter is null)
                continue;

            var start = events[i].ReceivedAtUtc;
            var end = events[i + 1].ReceivedAtUtc;

            // Subtract any bot-downtime overlap: during an outage the user's "leave" can
            // go unobserved, so the raw gap would over-credit voice time. We clamp out the
            // blind windows rather than drop the whole segment (mirrors the SQL
            // VoiceSegmentMinutes and the OnlineMinutesAsync downtime handling).
            var downSeconds = downtimes.Sum(d =>
            {
                var overlapStart = start > d.StartedAtUtc ? start : d.StartedAtUtc;
                var dEnd = d.EndedAtUtc ?? now;
                var overlapEnd = end < dEnd ? end : dEnd;
                return overlapEnd > overlapStart ? (overlapEnd - overlapStart).TotalSeconds : 0d;
            });

            var seconds = (end - start).TotalSeconds - downSeconds;
            if (seconds > 0)
                totalMinutes += seconds / 60.0;
        }

        return (long)Math.Round(totalMinutes, MidpointRounding.AwayFromZero);
    }

    // Best-effort approximation: Discord exposes no presence durations, so we sessionize
    // presence_events — each event contributes the gap to the next, counted only when the device
    // status is non-offline, the gap is at most 30 min (longer gaps are dropped, not clamped, to
    // keep stale/offline tails out), and the segment does not overlap a recorded bot-downtime.
    private async Task<long> OnlineMinutesAsync(ulong discordSf, CancellationToken ct)
    {
        var events = await db.PresenceEvents.AsNoTracking()
            .Where(p => p.UserDiscordId == discordSf)
            .OrderBy(p => p.ReceivedAtUtc)
            .Select(p => new
            {
                p.ReceivedAtUtc,
                Overall = Math.Max(Math.Max(p.DesktopStatusAfter, p.MobileStatusAfter), p.WebStatusAfter),
            })
            .ToListAsync(ct);

        var downtimes = await db.BotDowntimeIntervals.AsNoTracking()
            .Select(d => new { d.StartedAtUtc, d.EndedAtUtc })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var totalMinutes = 0d;
        for (var i = 0; i < events.Count - 1; i++)
        {
            var current = events[i];
            var next = events[i + 1];
            if (current.Overall <= 0)
                continue;

            var gap = next.ReceivedAtUtc - current.ReceivedAtUtc;
            if (gap > MaxPresenceGap)
                continue;

            var overlapsDowntime = downtimes.Any(d =>
                current.ReceivedAtUtc < (d.EndedAtUtc ?? now) && next.ReceivedAtUtc > d.StartedAtUtc);
            if (overlapsDowntime)
                continue;

            totalMinutes += gap.TotalMinutes;
        }

        return (long)Math.Round(totalMinutes, MidpointRounding.AwayFromZero);
    }

    // Last 14 CET days of the user's messages, zero-filled oldest -> newest. The day
    // series is anchored on "today" in guild-local time; each message timestamp is
    // converted to that zone and bucketed by its local calendar day.
    private async Task<IReadOnlyList<ProfileDailyPointDto>> MessagesDaily14Async(Guid userId, CancellationToken ct)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Tz);
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
        var startLocal = todayLocal.AddDays(-(SparklineDays - 1));
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), tz);

        var timestamps = await db.Messages.AsNoTracking()
            .Where(m => m.AuthorId == userId && m.CreatedAtUtc >= startUtc)
            .Select(m => m.CreatedAtUtc)
            .ToListAsync(ct);

        var counts = timestamps
            .GroupBy(ts => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ts, DateTimeKind.Utc), tz).Date)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var series = new List<ProfileDailyPointDto>(SparklineDays);
        for (var i = 0; i < SparklineDays; i++)
        {
            var day = startLocal.AddDays(i);
            counts.TryGetValue(day, out var count);
            series.Add(new ProfileDailyPointDto(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), count));
        }

        return series;
    }

    // Present reactions on this user's messages (see ReactionEventQueryExtensions).
    private async Task<long> CountReactionsReceivedAsync(Guid userId, CancellationToken ct) =>
        await db.ReactionEvents.AsNoTracking().WherePresent()
            .Join(
                db.Messages.AsNoTracking().Where(m => m.AuthorId == userId),
                r => r.MessageDiscordId,
                m => m.DiscordId,
                (r, m) => r.Id)
            .LongCountAsync(ct);

    // Top emote the user gave (present reactions only).
    private async Task<ProfileFavoriteEmoteDto?> GetFavoriteEmoteAsync(ulong discordSf, CancellationToken ct)
    {
        var favoriteEmote = await db.ReactionEvents.AsNoTracking().WherePresent()
            .Where(r => r.UserDiscordId == discordSf)
            .GroupBy(r => new { r.EmoteName, r.EmoteDiscordId })
            .Select(g => new { g.Key.EmoteName, g.Key.EmoteDiscordId, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync(ct);

        return favoriteEmote is null
            ? null
            : new ProfileFavoriteEmoteDto(
                favoriteEmote.EmoteName, favoriteEmote.EmoteDiscordId, favoriteEmote.EmoteDiscordId is > 0);
    }

    // Channel the user posted in most.
    private async Task<ProfileBusiestChannelDto?> GetBusiestChannelAsync(Guid userId, CancellationToken ct)
    {
        var busiestChannel = await db.Messages.AsNoTracking()
            .Where(m => m.AuthorId == userId)
            .GroupBy(m => new { m.Channel.Name, m.Channel.DiscordId })
            .Select(g => new { g.Key.Name, g.Key.DiscordId, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync(ct);

        return busiestChannel is null
            ? null
            : new ProfileBusiestChannelDto(busiestChannel.Name, busiestChannel.DiscordId);
    }

    // Current overall status from the latest presence row, by client-status priority
    // (online > idle > dnd > offline); "offline" when there are no presence rows.
    private async Task<string> GetCurrentStatusAsync(ulong discordSf, CancellationToken ct)
    {
        var latestPresence = await db.PresenceEvents.AsNoTracking()
            .Where(p => p.UserDiscordId == discordSf)
            .OrderByDescending(p => p.ReceivedAtUtc)
            .ThenByDescending(p => p.Id)
            .Select(p => new { p.DesktopStatusAfter, p.MobileStatusAfter, p.WebStatusAfter })
            .FirstOrDefaultAsync(ct);

        return latestPresence is null
            ? PresenceStatus.Offline
            : PresenceStatus.Overall(
                latestPresence.DesktopStatusAfter, latestPresence.MobileStatusAfter, latestPresence.WebStatusAfter);
    }

}
