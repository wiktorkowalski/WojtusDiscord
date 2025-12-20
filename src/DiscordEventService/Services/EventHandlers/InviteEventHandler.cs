using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class InviteEventHandler(IServiceScopeFactory scopeFactory, ILogger<InviteEventHandler> logger) :
    IEventHandler<InviteCreatedEventArgs>,
    IEventHandler<InviteDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, InviteCreatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "InviteCreated", e.Guild.Id, e.Channel.Id, e.Invite.Inviter?.Id);

            // Look up Guid FKs
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);
            var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);
            var inviter = e.Invite.Inviter != null
                ? await db.Users.FirstOrDefaultAsync(u => u.DiscordId == e.Invite.Inviter.Id)
                : null;

            // Upsert InviteEntity
            var invite = e.Invite;
            var inviteEntity = await db.Invites.FirstOrDefaultAsync(i => i.Code == invite.Code);
            if (inviteEntity == null)
            {
                inviteEntity = new InviteEntity { Code = invite.Code };
                db.Invites.Add(inviteEntity);
            }

            if (guild != null) inviteEntity.GuildId = guild.Id;
            if (channel != null) inviteEntity.ChannelId = channel.Id;
            inviteEntity.InviterId = inviter?.Id;
            inviteEntity.MaxAge = invite.MaxAge;
            inviteEntity.MaxUses = invite.MaxUses;
            inviteEntity.Uses = invite.Uses;
            inviteEntity.IsTemporary = invite.IsTemporary;
            inviteEntity.CreatedAtUtc = invite.CreatedAt.UtcDateTime;
            inviteEntity.ExpiresAtUtc = invite.MaxAge > 0 ? invite.CreatedAt.AddSeconds(invite.MaxAge).UtcDateTime : null;
            inviteEntity.IsDeleted = false;

            var eventEntity = new InviteEventEntity
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
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.InviteEvents.Add(eventEntity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling invite created");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, InviteDeletedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "InviteDeleted", e.Guild.Id, e.Channel.Id, null);

            // Mark invite as deleted
            await db.Invites
                .Where(i => i.Code == e.Invite.Code)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsDeleted, true));

            var eventEntity = new InviteEventEntity
            {
                GuildDiscordId = e.Guild.Id,
                ChannelDiscordId = e.Channel.Id,
                EventType = InviteEventType.Deleted,
                Code = e.Invite.Code,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.InviteEvents.Add(eventEntity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling invite deleted");
        }
    }
}
