using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public sealed class InviteEventHandler(EventPipeline pipeline) :
    IEventHandler<InviteCreatedEventArgs>,
    IEventHandler<InviteDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, InviteCreatedEventArgs e)
    {
        await pipeline.Execute(e, "InviteCreated", nameof(InviteEventHandler),
            e.Guild.Id, e.Channel.Id, e.Invite.Inviter?.Id, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var channelUpsert = ctx.Services.GetRequiredService<ChannelUpsertService>();
                var userService = ctx.Services.GetRequiredService<UserService>();

                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);
                var channelGuid = await channelUpsert.UpsertChannelAsync(e.Channel, guildGuid);
                Guid? inviterGuid = null;
                if (e.Invite.Inviter != null)
                {
                    await userService.UpsertUserAsync(e.Invite.Inviter);
                    inviterGuid = await ctx.Db.Users
                        .Where(u => u.DiscordId == e.Invite.Inviter.Id)
                        .Select(u => u.Id)
                        .FirstOrDefaultAsync();
                }

                var invite = e.Invite;
                var inviteEntity = await ctx.Db.Invites.FirstOrDefaultAsync(i => i.Code == invite.Code);
                if (inviteEntity == null)
                {
                    inviteEntity = new InviteEntity { Code = invite.Code };
                    ctx.Db.Invites.Add(inviteEntity);
                }

                if (guildGuid != Guid.Empty) inviteEntity.GuildId = guildGuid;
                if (channelGuid != Guid.Empty) inviteEntity.ChannelId = channelGuid;
                inviteEntity.InviterId = inviterGuid;
                inviteEntity.MaxAge = invite.MaxAge;
                inviteEntity.MaxUses = invite.MaxUses;
                inviteEntity.Uses = invite.Uses;
                inviteEntity.IsTemporary = invite.IsTemporary;
                inviteEntity.CreatedAtUtc = invite.CreatedAt.UtcDateTime;
                inviteEntity.ExpiresAtUtc = invite.MaxAge > 0 ? invite.CreatedAt.AddSeconds(invite.MaxAge).UtcDateTime : null;
                inviteEntity.IsDeleted = false;
                inviteEntity.DeletedAtUtc = null;

                ctx.Db.InviteEvents.Add(new InviteEventEntity
                {
                    GuildDiscordId = e.Guild.Id,
                    ChannelDiscordId = e.Channel.Id,
                    InviterDiscordId = invite.Inviter?.Id,
                    EventType = InviteEventType.Created,
                    Code = invite.Code,
                    MaxAge = invite.MaxAge,
                    MaxUses = invite.MaxUses,
                    IsTemporary = invite.IsTemporary,
                    Uses = invite.Uses,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, InviteDeletedEventArgs e)
    {
        await pipeline.Execute(e, "InviteDeleted", nameof(InviteEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                await ctx.Db.Invites
                    .Where(i => i.Code == e.Invite.Code)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.IsDeleted, true)
                        .SetProperty(i => i.DeletedAtUtc, (DateTime?)ctx.ReceivedAtUtc));

                ctx.Db.InviteEvents.Add(new InviteEventEntity
                {
                    GuildDiscordId = e.Guild.Id,
                    ChannelDiscordId = e.Channel.Id,
                    EventType = InviteEventType.Deleted,
                    Code = e.Invite.Code,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
