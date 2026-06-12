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
    public record Result(int ThreadsRepaired);

    public async Task<Result> BackfillAsync(CancellationToken ct)
    {
        var repaired = await BackfillIncompleteThreadChannelsAsync(ct);
        return new Result(repaired);
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

        var repaired = 0;
        foreach (var thread in incomplete)
        {
            try
            {
                var live = await client.GetChannelAsync(thread.DiscordId);
                await channelUpsert.UpsertChannelAsync(live, thread.GuildId);
                repaired++;
                logger.LogInformation(
                    "Repaired thread channel {ChannelId} via Discord API as {ChannelType} under parent {ParentChannelId}",
                    thread.DiscordId, live.Type, live.ParentId);
            }
            catch (NotFoundException)
            {
                logger.LogDebug("Thread {ChannelId} 404 from Discord; leaving as-is", thread.DiscordId);
            }
            catch (UnauthorizedException)
            {
                logger.LogDebug("Thread {ChannelId} 403 from Discord; leaving as-is", thread.DiscordId);
            }
        }

        return repaired;
    }
}
