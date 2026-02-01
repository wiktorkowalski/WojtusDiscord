using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public class MembersBackfillJob(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<MembersBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Members;

    public async Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();

        var checkpoint = await GetOrCreateCheckpointAsync(db, guildId);
        checkpoint.Status = BackfillStatus.InProgress;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var guild = await discordClient.GetGuildAsync(guildId);
            var guildEntity = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
            {
                logger.LogWarning("Guild {GuildId} not found in database, cannot backfill members", guildId);
                await MarkFailedAsync(db, checkpoint, new InvalidOperationException($"Guild {guildId} not found in database"));
                return;
            }

            // Get all members - requires GUILD_MEMBERS privileged intent
            // DSharpPlus v5 returns IAsyncEnumerable
            var membersList = new List<DSharpPlus.Entities.DiscordMember>();
            await foreach (var member in guild.GetAllMembersAsync())
            {
                membersList.Add(member);
            }

            checkpoint.TotalCount = membersList.Count;
            await db.SaveChangesAsync(cancellationToken);

            foreach (var member in membersList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await userService.UpsertMemberAsync(member);
                    checkpoint.ProcessedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to upsert member {MemberId} in guild {GuildId}", member.Id, guildId);
                    await RecordErrorAsync(db, checkpoint, ex);
                }

                // Save progress periodically (every 100 members)
                if (checkpoint.ProcessedCount % 100 == 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            await MarkCompletedAsync(db, checkpoint);
            logger.LogInformation("Members backfill completed for guild {GuildId}: {Count} members", guildId, checkpoint.ProcessedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Members backfill failed for guild {GuildId}", guildId);
            await MarkFailedAsync(db, checkpoint, ex);
            throw;
        }
    }
}
