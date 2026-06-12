using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Jobs;

// Weekly healing sweep: the live MessageCreated hook indexes new memes within
// seconds; this catches what it misses — attachments posted during downtime,
// live-path enqueue failures, and Failed rows still under the attempt cap.
// Skips guilds whose meme indexing is already running.
public sealed class MemeIndexSweepJob(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobClient backgroundJobClient,
    ILogger<MemeIndexSweepJob> logger)
{
    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var memeOptions = scope.ServiceProvider.GetRequiredService<IOptions<MemeIndexOptions>>().Value;
        var openRouterOptions = scope.ServiceProvider.GetRequiredService<IOptions<OpenRouterOptions>>().Value;

        // Checked here, not just in the per-guild job: an unconfigured deploy
        // (prod until #223) must not write a Failed checkpoint every week.
        if (!memeOptions.IsConfigured || !openRouterOptions.IsConfigured
            || string.IsNullOrWhiteSpace(openRouterOptions.Model))
        {
            logger.LogInformation("Meme index sweep skipped: meme indexing is not fully configured");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var channelIds = memeOptions.ChannelIds;

        var guildIds = await db.Channels.AsNoTracking()
            .Where(c => channelIds.Contains(c.DiscordId))
            .Join(db.Guilds.AsNoTracking().Where(g => g.LeftAtUtc == null),
                c => c.GuildId, g => g.Id, (c, g) => g.DiscordId)
            .Distinct()
            .ToListAsync();

        var inProgress = await db.BackfillCheckpoints.AsNoTracking()
            .Where(c => c.Type == BackfillType.MemeIndex && c.Status == BackfillStatus.InProgress)
            .Select(c => c.GuildDiscordId)
            .ToListAsync();
        var inProgressSet = inProgress.ToHashSet();

        foreach (var guildId in guildIds)
        {
            if (inProgressSet.Contains(guildId))
            {
                logger.LogInformation(
                    "Meme index sweep skipped for guild {GuildId}: indexing already in progress", guildId);
                continue;
            }

            backgroundJobClient.Enqueue<MemeIndexingJob>(j => j.ExecuteSweepAsync(guildId, CancellationToken.None));
            logger.LogInformation("Meme index sweep enqueued for guild {GuildId}", guildId);
        }
    }
}
