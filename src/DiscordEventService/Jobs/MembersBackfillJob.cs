using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public sealed class MembersBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor,
    ILogger<MembersBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Members;

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => executor.RunAsync(BackfillType, guildId, async ctx =>
        {
            var userService = ctx.Services.GetRequiredService<UserService>();

            var guild = await discordClient.GetGuildAsync(guildId);
            var guildEntity = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
                return BackfillOutcome.ShortCircuit($"Guild {guildId} not found in database");

            // Get all members - requires GUILD_MEMBERS privileged intent
            // DSharpPlus v5 returns IAsyncEnumerable
            var membersList = new List<DSharpPlus.Entities.DiscordMember>();
            await foreach (var member in guild.GetAllMembersAsync())
            {
                membersList.Add(member);
            }

            ctx.Checkpoint.TotalCount = membersList.Count;
            await ctx.Db.SaveChangesAsync(cancellationToken);

            foreach (var member in membersList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await userService.UpsertMemberAsync(member);
                    ctx.Checkpoint.ProcessedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to upsert member {MemberId} in guild {GuildId}", member.Id, guildId);
                    await RecordErrorAsync(ctx.Db, ctx.Checkpoint, ex);
                }

                // Save progress periodically (every 100 members)
                if (ctx.Checkpoint.ProcessedCount % 100 == 0)
                    await ctx.Db.SaveChangesAsync(cancellationToken);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);
}
