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
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();
                var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                var channelUpsert = scope.ServiceProvider.GetRequiredService<ChannelUpsertService>();
                var userService = scope.ServiceProvider.GetRequiredService<UserService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "InviteCreated", e.Guild.Id, e.Channel.Id, e.Invite.Inviter?.Id, correlationId: correlationId);

                await db.SaveChangesAsync();

                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);
                var channelGuid = await channelUpsert.UpsertChannelAsync(e.Channel, guildGuid);
                Guid? inviterGuid = null;
                if (e.Invite.Inviter != null)
                {
                    await userService.UpsertUserAsync(e.Invite.Inviter);
                    inviterGuid = await db.Users
                        .Where(u => u.DiscordId == e.Invite.Inviter.Id)
                        .Select(u => u.Id)
                        .FirstOrDefaultAsync();
                }

                // Upsert InviteEntity
                var invite = e.Invite;
                var inviteEntity = await db.Invites.FirstOrDefaultAsync(i => i.Code == invite.Code);
                if (inviteEntity == null)
                {
                    inviteEntity = new InviteEntity { Code = invite.Code };
                    db.Invites.Add(inviteEntity);
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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "InviteCreated", nameof(InviteEventHandler), ex,
                    e.Guild?.Id, e.Channel?.Id, e.Invite?.Inviter?.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, InviteDeletedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "InviteDeleted", e.Guild.Id, e.Channel.Id, null, correlationId: correlationId);

                // Mark invite as deleted
                await db.Invites
                    .Where(i => i.Code == e.Invite.Code)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.IsDeleted, true)
                        .SetProperty(i => i.DeletedAtUtc, (DateTime?)now));

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "InviteDeleted", nameof(InviteEventHandler), ex,
                    e.Guild?.Id, e.Channel?.Id, null, rawJson, correlationId: correlationId);
            }
        }
    }
}
