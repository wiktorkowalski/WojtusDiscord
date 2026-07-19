using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class InviteEventHandler(EventPipeline pipeline) :
    IEventHandler<InviteCreatedEventArgs>,
    IEventHandler<InviteDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, InviteCreatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "InviteCreated", nameof(InviteEventHandler),
            e.Guild.Id, e.Channel.Id, e.Invite.Inviter?.Id, async ctx =>
            {
                var userService = ctx.Services.GetRequiredService<UserService>();

                // Resolve the required guild+channel FKs via the shared resolver: on a miss it
                // logs and records a FailedEvent (replayable trace) instead of staging an invites
                // row with Guid.Empty FKs (#292). The InviteEvent timeline row is FK-free
                // (snowflake columns) and still lands below.
                var invite = e.Invite;
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, e.Channel, $"InviteCreated Code={invite.Code}");

                if (fks.Success)
                {
                    Guid? inviterGuid = null;
                    if (invite.Inviter is not null)
                    {
                        var inviterResult = await userService.UpsertUserAsync(invite.Inviter);
                        inviterGuid = inviterResult.IsSuccess ? inviterResult.Value : null;
                    }

                    var inviteEntity = await ctx.Db.Invites.FirstOrDefaultAsync(i => i.Code == invite.Code);
                    if (inviteEntity is null)
                    {
                        inviteEntity = new InviteEntity { Code = invite.Code };
                        ctx.Db.Invites.Add(inviteEntity);
                    }

                    inviteEntity.GuildId = fks.GuildId;
                    inviteEntity.ChannelId = fks.ChannelId;
                    inviteEntity.InviterId = inviterGuid;
                    inviteEntity.MaxAge = invite.MaxAge;
                    inviteEntity.MaxUses = invite.MaxUses;
                    inviteEntity.Uses = invite.Uses;
                    inviteEntity.IsTemporary = invite.IsTemporary;
                    inviteEntity.CreatedAtUtc = invite.CreatedAt.UtcDateTime;
                    inviteEntity.ExpiresAtUtc = invite.MaxAge > 0 ? invite.CreatedAt.AddSeconds(invite.MaxAge).UtcDateTime : null;
                    inviteEntity.IsDeleted = false;
                    inviteEntity.DeletedAtUtc = null;
                }

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
                    // Discord supplies the invite's creation time — the true event time for a
                    // creation (CONTEXT.md). Fall back to receive time only if it is absent.
                    EventTimestampUtc = invite.CreatedAt != default
                        ? invite.CreatedAt.UtcDateTime
                        : ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, InviteDeletedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "InviteDeleted", nameof(InviteEventHandler),
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
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
