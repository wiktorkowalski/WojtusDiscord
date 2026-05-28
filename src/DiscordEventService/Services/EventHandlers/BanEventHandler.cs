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
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var userService = ctx.Services.GetRequiredService<UserService>();

                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);
                await userService.UpsertUserAsync(e.Member);
                var userGuid = await ctx.Db.Users
                    .Where(u => u.DiscordId == e.Member.Id)
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync();

                if (guildGuid != Guid.Empty && userGuid != Guid.Empty)
                {
                    var existingBan = await ctx.Db.Bans
                        .FirstOrDefaultAsync(b => b.GuildId == guildGuid && b.UserId == userGuid && b.IsActive);

                    if (existingBan == null)
                    {
                        ctx.Db.Bans.Add(new BanEntity
                        {
                            GuildId = guildGuid,
                            UserId = userGuid,
                            IsActive = true,
                            BannedAtUtc = ctx.ReceivedAtUtc
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
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildBanRemovedEventArgs e)
    {
        await pipeline.Execute(e, "GuildBanRemoved", nameof(BanEventHandler),
            e.Guild.Id, null, e.Member.Id, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var userService = ctx.Services.GetRequiredService<UserService>();

                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);
                await userService.UpsertUserAsync(e.Member);
                var userGuid = await ctx.Db.Users
                    .Where(u => u.DiscordId == e.Member.Id)
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync();

                if (guildGuid != Guid.Empty && userGuid != Guid.Empty)
                {
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
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
