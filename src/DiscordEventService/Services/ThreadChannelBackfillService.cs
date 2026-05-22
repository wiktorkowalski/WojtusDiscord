using DiscordEventService.Data;
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
    public record Result(int OrphansScanned, int ChannelsFetched, int Placeholders, int MessagesLinked, int Unresolved);

    public async Task<Result> BackfillAsync(CancellationToken ct)
    {
        var orphans = await db.Messages
            .Where(m => m.ChannelId == null)
            .Select(m => new { m.Id, m.DiscordId, m.FirstSeenUtc, m.GuildId })
            .ToListAsync(ct);

        if (orphans.Count == 0)
        {
            return new Result(0, 0, 0, 0, 0);
        }

        // For each orphan, resolve both the missing channel_discord_id AND the guild_discord_id
        // from the matching MessageCreated raw event (deterministic: nearest received_at_utc
        // within ±2s). Carrying the guild_discord_id avoids the "pick an arbitrary guild" trap.
        var orphanResolution = new Dictionary<Guid, (ulong ChannelDiscordId, ulong GuildDiscordId)>();
        foreach (var orphan in orphans)
        {
            var windowStart = orphan.FirstSeenUtc.AddSeconds(-2);
            var windowEnd = orphan.FirstSeenUtc.AddSeconds(2);

            var candidates = await db.RawEventLogs
                .Where(r => r.EventType == "MessageCreated"
                    && r.ReceivedAtUtc >= windowStart
                    && r.ReceivedAtUtc <= windowEnd
                    && r.ChannelDiscordId != null)
                .Select(r => new { r.ChannelDiscordId, r.GuildDiscordId, r.ReceivedAtUtc })
                .ToListAsync(ct);

            var best = candidates
                .OrderBy(c => Math.Abs((c.ReceivedAtUtc - orphan.FirstSeenUtc).TotalMilliseconds))
                .FirstOrDefault();

            if (best is { ChannelDiscordId: { } cid })
            {
                orphanResolution[orphan.Id] = (cid, best.GuildDiscordId);
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
                .Select(c => c.Id)
                .FirstOrDefaultAsync(ct);

            if (existing != Guid.Empty)
            {
                resolvedChannelGuids[entry.ChannelDiscordId] = existing;
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
                .Select(g => g.Id)
                .FirstOrDefaultAsync(ct);

            if (guildGuid == Guid.Empty)
            {
                logger.LogError(
                    "Guild {GuildDiscordId} for orphan channel {ChannelDiscordId} not present in DB; skipping",
                    guildDiscordId, entry.ChannelDiscordId);
                continue;
            }

            try
            {
                var live = await client.GetChannelAsync(entry.ChannelDiscordId);
                var id = await channelUpsert.UpsertChannelAsync(live, guildGuid);
                resolvedChannelGuids[entry.ChannelDiscordId] = id;
                fetched++;
                logger.LogInformation("Backfilled channel {DiscordId} via Discord API as {Type}", entry.ChannelDiscordId, live.Type);
                continue;
            }
            catch (NotFoundException)
            {
                // Channel genuinely gone from Discord's side — placeholder is the only honest option.
                logger.LogWarning(
                    "Discord 404 for {DiscordId}; inserting placeholder",
                    entry.ChannelDiscordId);
            }
            catch (UnauthorizedException)
            {
                // Bot doesn't currently have permission — also placeholder territory, but log loud.
                logger.LogWarning(
                    "Discord 403 for {DiscordId}; inserting placeholder",
                    entry.ChannelDiscordId);
            }

            var firstOrphan = orphans
                .Where(o => orphanResolution.TryGetValue(o.Id, out var c) && c.ChannelDiscordId == entry.ChannelDiscordId)
                .Min(o => o.FirstSeenUtc);
            var placeholderId = await channelUpsert.InsertPlaceholderAsync(entry.ChannelDiscordId, guildGuid, firstOrphan);
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
        return new Result(orphans.Count, fetched, placeholders, linked, unresolved);
    }
}
