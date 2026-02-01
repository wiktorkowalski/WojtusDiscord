using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class GuildUpdateEventHandler(IServiceScopeFactory scopeFactory, ILogger<GuildUpdateEventHandler> logger) :
    IEventHandler<GuildUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildUpdatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildUpdatedEvent", e.GuildAfter.Id, null, null);

            var guildEvent = new GuildEventEntity
            {
                GuildDiscordId = e.GuildAfter.Id,
                EventType = GuildEventType.Updated,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            if (e.GuildBefore.Name != e.GuildAfter.Name)
            {
                guildEvent.NameBefore = e.GuildBefore.Name;
                guildEvent.NameAfter = e.GuildAfter.Name;
            }

            if (e.GuildBefore.IconHash != e.GuildAfter.IconHash)
            {
                guildEvent.IconHashBefore = e.GuildBefore.IconHash;
                guildEvent.IconHashAfter = e.GuildAfter.IconHash;
            }

            if (e.GuildBefore.OwnerId != e.GuildAfter.OwnerId)
            {
                guildEvent.OwnerDiscordIdBefore = e.GuildBefore.OwnerId;
                guildEvent.OwnerDiscordIdAfter = e.GuildAfter.OwnerId;
            }

            db.GuildEvents.Add(guildEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling guild updated for GuildId={GuildId}", e.GuildAfter.Id);
        }
    }
}
