using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public sealed class BanEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildBanAddedEventArgs>,
    IEventHandler<GuildBanRemovedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildBanAddedEventArgs e)
    {
        await pipeline.Execute(e, "GuildBanAdded", nameof(BanEventHandler),
            e.Guild.Id, null, e.Member.Id, async ctx =>
            {
                // Resolve the required guild+user FKs via the shared resolver: on a miss it logs and
                // records a FailedEvent (replayable trace) instead of silently dropping the bans row.
                // The BanEvent timeline row is FK-free (snowflake columns) and still lands below, so a
                // resolve miss costs only the derived bans row, not the faithful timeline record.
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, e.Member, $"GuildId={e.Guild.Id} UserId={e.Member.Id}");

                if (fks.Success)
                {
                    var guildGuid = fks.GuildId;
                    var userGuid = fks.UserId;

                    var existingBan = await ctx.Db.Bans
                        .FirstOrDefaultAsync(b => b.GuildId == guildGuid && b.UserId == userGuid && b.IsActive);

                    if (existingBan == null)
                    {
                        ctx.Db.Bans.Add(new BanEntity
                        {
                            GuildId = guildGuid,
                            UserId = userGuid,
                            IsActive = true,
                            BannedAtUtc = ctx.ReceivedAtUtc,
                        });
                    }
                }

                ctx.Db.BanEvents.Add(new BanEventEntity
                {
                    GuildDiscordId = e.Guild.Id,
                    UserDiscordId = e.Member.Id,
                    EventType = BanEventType.Added,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanRemovedEventArgs e)
    {
        await pipeline.Execute(e, "GuildBanRemoved", nameof(BanEventHandler),
            e.Guild.Id, null, e.Member.Id, async ctx =>
            {
                // See GuildBanAdded: resolve guild+user via the shared resolver so a miss is logged +
                // recorded as a FailedEvent rather than silently skipping the unban. The BanEvent row
                // is FK-free and still lands below regardless of resolution.
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, e.Member, $"GuildId={e.Guild.Id} UserId={e.Member.Id}");

                if (fks.Success)
                {
                    var guildGuid = fks.GuildId;
                    var userGuid = fks.UserId;

                    await ctx.Db.Bans
                        .Where(b => b.GuildId == guildGuid && b.UserId == userGuid && b.IsActive)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.IsActive, false)
                            .SetProperty(b => b.UnbannedAtUtc, ctx.ReceivedAtUtc));
                }

                ctx.Db.BanEvents.Add(new BanEventEntity
                {
                    GuildDiscordId = e.Guild.Id,
                    UserDiscordId = e.Member.Id,
                    EventType = BanEventType.Removed,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
