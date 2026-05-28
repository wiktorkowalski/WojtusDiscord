using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using DSharpPlus.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public class ThreadChannelBackfillService(
    DiscordDbContext db,
    DiscordClient client,
    ChannelUpsertService channelUpsert,
    ILogger<ThreadChannelBackfillService> logger)
{
    public record Result(
        int OrphansScanned,
        int ChannelsFetched,
        int Placeholders,
        int MessagesLinked,
        int Unresolved,
        int ExistingThreadsRepaired);

    public async Task<Result> BackfillAsync(CancellationToken ct)
    {
        var orphanResult = await BackfillOrphansAsync(ct);
        var repaired = await BackfillIncompleteThreadChannelsAsync(ct);
        return orphanResult with { ExistingThreadsRepaired = repaired };
    }

    private async Task<Result> BackfillOrphansAsync(CancellationToken ct)
    {
        var orphans = await db.Messages
            .Where(m => m.ChannelId == null)
            .Select(m => new { m.Id, m.DiscordId, m.FirstSeenUtc, m.GuildId })
            .ToListAsync(ct);

        if (orphans.Count == 0)
        {
            return new Result(0, 0, 0, 0, 0, 0);
        }

        // Hybrid resolver: try a deterministic match first (event_json->'message'->>'id' = orphan.DiscordId),
        // fall back to nearest-timestamp ±2s when the raw JSON is stub-fallback (pre-#99 serializer bug).
        var orphanResolution = new Dictionary<Guid, (ulong ChannelDiscordId, ulong GuildDiscordId)>();
        foreach (var orphan in orphans)
        {
            var resolved = await ResolveOrphanChannelAsync(orphan.DiscordId, orphan.FirstSeenUtc, ct);
            if (resolved is var (cid, gid))
            {
                orphanResolution[orphan.Id] = (cid, gid);
            }
            else
            {
                logger.LogWarning(
                    "Could not resolve channel_discord_id for orphan message {MessageId} (DiscordId={DiscordId})",
                    orphan.Id, orphan.DiscordId);
            }
        }

        // Group orphans by (channel, guild) so we upsert each channel under its own guild.
        var uniqueChannels = orphanResolution
            .Values
            .GroupBy(v => v.ChannelDiscordId)
            .Select(g => new
            {
                ChannelDiscordId = g.Key,
                GuildDiscordIds = g.Select(v => v.GuildDiscordId).Distinct().ToList()
            })
            .ToList();

        int fetched = 0;
        int placeholders = 0;
        var resolvedChannelGuids = new Dictionary<ulong, Guid>();
        foreach (var entry in uniqueChannels)
        {
            var existing = await db.Channels
                .Where(c => c.DiscordId == entry.ChannelDiscordId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                resolvedChannelGuids[entry.ChannelDiscordId] = existing.Value;
                continue;
            }

            if (entry.GuildDiscordIds.Count != 1)
            {
                logger.LogWarning(
                    "Orphan channel {ChannelId} appears under multiple guild_discord_ids ({Count}); using first",
                    entry.ChannelDiscordId, entry.GuildDiscordIds.Count);
            }
            var guildDiscordId = entry.GuildDiscordIds[0];
            var guildGuid = await db.Guilds
                .Where(g => g.DiscordId == guildDiscordId)
                .Select(g => (Guid?)g.Id)
                .FirstOrDefaultAsync(ct);

            if (guildGuid is null)
            {
                logger.LogError(
                    "Guild {GuildDiscordId} for orphan channel {ChannelDiscordId} not present in DB; skipping",
                    guildDiscordId, entry.ChannelDiscordId);
                continue;
            }

            try
            {
                var live = await client.GetChannelAsync(entry.ChannelDiscordId);
                var id = (await channelUpsert.UpsertChannelAsync(live, guildGuid.Value)).Value;
                resolvedChannelGuids[entry.ChannelDiscordId] = id;
                fetched++;
                logger.LogInformation("Backfilled channel {DiscordId} via Discord API as {Type}", entry.ChannelDiscordId, live.Type);
                continue;
            }
            catch (NotFoundException)
            {
                logger.LogWarning(
                    "Discord 404 for {DiscordId}; inserting placeholder",
                    entry.ChannelDiscordId);
            }
            catch (UnauthorizedException)
            {
                logger.LogWarning(
                    "Discord 403 for {DiscordId}; inserting placeholder",
                    entry.ChannelDiscordId);
            }

            var firstOrphan = orphans
                .Where(o => orphanResolution.TryGetValue(o.Id, out var c) && c.ChannelDiscordId == entry.ChannelDiscordId)
                .Min(o => o.FirstSeenUtc);
            var placeholderId = await channelUpsert.InsertPlaceholderAsync(entry.ChannelDiscordId, guildGuid.Value, firstOrphan);
            resolvedChannelGuids[entry.ChannelDiscordId] = placeholderId;
            placeholders++;
        }

        int linked = 0;
        foreach (var (orphanId, (channelDiscordId, _)) in orphanResolution)
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

        var unresolved = orphans.Count - orphanResolution.Count;
        return new Result(orphans.Count, fetched, placeholders, linked, unresolved, 0);
    }

    private async Task<(ulong ChannelDiscordId, ulong GuildDiscordId)?> ResolveOrphanChannelAsync(
        ulong orphanDiscordId, DateTime orphanFirstSeenUtc, CancellationToken ct)
    {
        // Deterministic match via JSON message-id (post-#99 events have intact JSON).
        var idText = orphanDiscordId.ToString();
        var jsonMatch = await db.RawEventLogs
            .FromSqlInterpolated($@"
                SELECT *
                FROM raw_event_logs
                WHERE event_type = 'MessageCreated'
                  AND channel_discord_id IS NOT NULL
                  AND event_json->'message'->>'id' = {idText}
                LIMIT 1")
            .Select(r => new { r.ChannelDiscordId, r.GuildDiscordId })
            .FirstOrDefaultAsync(ct);

        if (jsonMatch is { ChannelDiscordId: { } jsonCid })
        {
            return (jsonCid, jsonMatch.GuildDiscordId);
        }

        // Fallback: nearest-timestamp match within ±2s — only path that works for pre-#99 stub JSON.
        var windowStart = orphanFirstSeenUtc.AddSeconds(-2);
        var windowEnd = orphanFirstSeenUtc.AddSeconds(2);

        var candidates = await db.RawEventLogs
            .Where(r => r.EventType == "MessageCreated"
                && r.ReceivedAtUtc >= windowStart
                && r.ReceivedAtUtc <= windowEnd
                && r.ChannelDiscordId != null)
            .Select(r => new { r.ChannelDiscordId, r.GuildDiscordId, r.ReceivedAtUtc })
            .ToListAsync(ct);

        var best = candidates
            .OrderBy(c => Math.Abs((c.ReceivedAtUtc - orphanFirstSeenUtc).TotalMilliseconds))
            .FirstOrDefault();

        return best is { ChannelDiscordId: { } cid } ? (cid, best.GuildDiscordId) : null;
    }

    private async Task<int> BackfillIncompleteThreadChannelsAsync(CancellationToken ct)
    {
        // Any thread channel (type ∈ {10,11,12}) missing parent_discord_id, or whose name is the
        // placeholder we insert when Discord can't fetch it — try to re-fetch via the API and refresh.
        var incomplete = await db.Channels
            .Where(c => (c.Type == ChannelType.NewsThread || c.Type == ChannelType.PublicThread || c.Type == ChannelType.PrivateThread)
                && (c.ParentDiscordId == null || c.Name.StartsWith("[unknown thread")))
            .Select(c => new { c.Id, c.DiscordId, c.GuildId })
            .ToListAsync(ct);

        int repaired = 0;
        foreach (var thread in incomplete)
        {
            try
            {
                var live = await client.GetChannelAsync(thread.DiscordId);
                await channelUpsert.UpsertChannelAsync(live, thread.GuildId);
                repaired++;
                logger.LogInformation(
                    "Repaired thread channel {DiscordId} via Discord API as {Type} (parent={ParentId})",
                    thread.DiscordId, live.Type, live.ParentId);
            }
            catch (NotFoundException)
            {
                logger.LogDebug("Thread {DiscordId} 404 from Discord; leaving as-is", thread.DiscordId);
            }
            catch (UnauthorizedException)
            {
                logger.LogDebug("Thread {DiscordId} 403 from Discord; leaving as-is", thread.DiscordId);
            }
        }

        return repaired;
    }
}
