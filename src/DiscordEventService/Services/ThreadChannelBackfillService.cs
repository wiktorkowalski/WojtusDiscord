using DiscordEventService.Data;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public class ThreadChannelBackfillService(
    DiscordDbContext db,
    DiscordClient client,
    ChannelUpsertService channelUpsert,
    ILogger<ThreadChannelBackfillService> logger)
{
    public record Result(int OrphansScanned, int ChannelsFetched, int Placeholders, int MessagesLinked, int Unresolved);

    public async Task<Result> BackfillAsync(CancellationToken ct)
    {
        var orphans = await db.Messages
            .Where(m => m.ChannelId == null)
            .Select(m => new { m.Id, m.DiscordId, m.FirstSeenUtc })
            .ToListAsync(ct);

        if (orphans.Count == 0)
        {
            return new Result(0, 0, 0, 0, 0);
        }

        // Resolve each orphan's channel_discord_id deterministically: pick the MessageCreated
        // raw event within ±2s whose received_at_utc is closest to the orphan's first_seen_utc.
        var orphanToChannel = new Dictionary<Guid, ulong>();
        foreach (var orphan in orphans)
        {
            var windowStart = orphan.FirstSeenUtc.AddSeconds(-2);
            var windowEnd = orphan.FirstSeenUtc.AddSeconds(2);

            var candidates = await db.RawEventLogs
                .Where(r => r.EventType == "MessageCreated"
                    && r.ReceivedAtUtc >= windowStart
                    && r.ReceivedAtUtc <= windowEnd
                    && r.ChannelDiscordId != null)
                .Select(r => new { r.ChannelDiscordId, r.ReceivedAtUtc })
                .ToListAsync(ct);

            var best = candidates
                .OrderBy(c => Math.Abs((c.ReceivedAtUtc - orphan.FirstSeenUtc).TotalMilliseconds))
                .FirstOrDefault();

            if (best is { ChannelDiscordId: { } cid })
            {
                orphanToChannel[orphan.Id] = cid;
            }
            else
            {
                logger.LogWarning(
                    "Could not resolve channel_discord_id for orphan message {MessageId} (DiscordId={DiscordId})",
                    orphan.Id, orphan.DiscordId);
            }
        }

        var uniqueChannels = orphanToChannel.Values.Distinct().ToList();
        var guildId = await db.Guilds.Select(g => g.Id).FirstAsync(ct);

        // Ensure each unique channel exists in `channels`. Try Discord API; fall back to placeholder.
        int fetched = 0;
        int placeholders = 0;
        var resolvedChannelGuids = new Dictionary<ulong, Guid>();
        foreach (var discordId in uniqueChannels)
        {
            var existing = await db.Channels
                .Where(c => c.DiscordId == discordId)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(ct);

            if (existing != Guid.Empty)
            {
                resolvedChannelGuids[discordId] = existing;
                continue;
            }

            try
            {
                var live = await client.GetChannelAsync(discordId);
                if (live is not null)
                {
                    var id = await channelUpsert.UpsertChannelAsync(live, guildId);
                    resolvedChannelGuids[discordId] = id;
                    fetched++;
                    logger.LogInformation("Backfilled channel {DiscordId} via Discord API as {Type}", discordId, live.Type);
                    continue;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Discord API GetChannelAsync failed for {DiscordId}; inserting placeholder",
                    discordId);
            }

            var firstOrphan = orphans
                .Where(o => orphanToChannel.TryGetValue(o.Id, out var c) && c == discordId)
                .Min(o => o.FirstSeenUtc);
            var placeholderId = await channelUpsert.InsertPlaceholderAsync(discordId, guildId, firstOrphan);
            resolvedChannelGuids[discordId] = placeholderId;
            placeholders++;
        }

        // Link orphans to their resolved channels.
        int linked = 0;
        foreach (var (orphanId, channelDiscordId) in orphanToChannel)
        {
            if (!resolvedChannelGuids.TryGetValue(channelDiscordId, out var channelGuid))
            {
                continue;
            }

            var rows = await db.Messages
                .Where(m => m.Id == orphanId && m.ChannelId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ChannelId, channelGuid), ct);
            linked += rows;
        }

        var unresolved = orphans.Count - orphanToChannel.Count;
        return new Result(orphans.Count, fetched, placeholders, linked, unresolved);
    }
}
