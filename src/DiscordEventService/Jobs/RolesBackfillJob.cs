using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

internal sealed class RolesBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Roles;

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => executor.RunAsync(BackfillType, guildId, async ctx =>
        {
            var guild = await discordClient.GetGuildAsync(guildId);
            var guildEntity = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
                return BackfillOutcome.ShortCircuit($"Guild {guildId} not found in database");

            ctx.Checkpoint.TotalCount = guild.Roles.Count;
            await ctx.Db.SaveChangesAsync(cancellationToken);

            foreach (var role in guild.Roles.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var permissions = long.TryParse(role.Permissions.ToString(), out var perm) ? perm : 0;

                await ctx.Db.Roles.UpsertAsync(
                    r => r.DiscordId == role.Id,
                    s => s
                        .SetProperty(r => r.Name, role.Name)
                        .SetProperty(r => r.Color, role.Color.Value)
                        .SetProperty(r => r.IsHoisted, role.IsHoisted)
                        .SetProperty(r => r.Position, role.Position)
                        .SetProperty(r => r.Permissions, permissions)
                        .SetProperty(r => r.IsManaged, role.IsManaged)
                        .SetProperty(r => r.IsMentionable, role.IsMentionable)
                        .SetProperty(r => r.IsDeleted, false)
                        .SetProperty(r => r.DeletedAtUtc, (DateTime?)null),
                    () => new RoleEntity
                    {
                        DiscordId = role.Id,
                        GuildId = guildEntity.Id,
                        Name = role.Name,
                        Color = role.Color.Value,
                        IsHoisted = role.IsHoisted,
                        Position = role.Position,
                        Permissions = permissions,
                        IsManaged = role.IsManaged,
                        IsMentionable = role.IsMentionable
                    },
                    r => r.Id,
                    cancellationToken);

                ctx.Checkpoint.ProcessedCount++;

                if (ctx.Checkpoint.ProcessedCount % 50 == 0)
                    await SaveProgressAsync(ctx.Db, ctx.Checkpoint);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);
}
