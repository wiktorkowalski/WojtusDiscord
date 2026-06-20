using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.Conversation;

internal sealed record PosterStat(string Username, ulong UserDiscordId, long MessageCount);

// Curated, typed leaderboard queries for the conversational assistant (#238 §4): the safe,
// type-safe complement to the query_database SQL escape hatch. The model supplies typed arguments
// (no SQL) and EF builds a parameterized query — zero injection surface. Reuses the same message/
// channel/user shape the dashboard's People stats use.
internal sealed class GuildStatsService(DiscordDbContext db)
{
    public const int MaxLimit = 25;

    // Most active members by message count, optionally within the last N days and/or a channel whose
    // name contains a fragment. Ranking is done in SQL (Take), then usernames are resolved in a second
    // cheap query so the ranked order is preserved deterministically.
    public async Task<IReadOnlyList<PosterStat>> TopPostersAsync(
        ulong guildDiscordId, int limit, int sinceDays, string? channelNameContains, CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaxLimit);

        var guildId = await db.Guilds
            .Where(g => g.DiscordId == guildDiscordId)
            .Select(g => g.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (guildId == Guid.Empty)
            return [];

        var query = db.Messages.AsNoTracking().Where(m => m.GuildId == guildId);

        if (sinceDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-sinceDays);
            query = query.Where(m => m.CreatedAtUtc >= cutoff);
        }

        if (!string.IsNullOrWhiteSpace(channelNameContains))
        {
            var pattern = $"%{channelNameContains.Trim()}%";
            var channelIds = await db.Channels
                .Where(c => c.GuildId == guildId && EF.Functions.ILike(c.Name, pattern))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            if (channelIds.Count == 0)
                return [];
            query = query.Where(m => channelIds.Contains(m.ChannelId));
        }

        var ranked = await query
            .GroupBy(m => m.AuthorId)
            .Select(group => new { AuthorId = group.Key, Count = group.LongCount() })
            // AuthorId tiebreak so the Take cutoff is deterministic across calls when counts tie.
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.AuthorId)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
        if (ranked.Count == 0)
            return [];

        var authorIds = ranked.Select(r => r.AuthorId).ToList();
        var users = await db.Users
            .Where(u => authorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username, u.DiscordId })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return ranked
            .Where(r => users.ContainsKey(r.AuthorId))
            .Select(r => new PosterStat(users[r.AuthorId].Username, users[r.AuthorId].DiscordId, r.Count))
            .ToList();
    }
}
