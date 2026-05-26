using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public class EmojiEventHandler(IServiceScopeFactory scopeFactory, ILogger<EmojiEventHandler> logger) :
    IEventHandler<GuildEmojisUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildEmojisUpdatedEventArgs e)
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
                    e, "GuildEmojisUpdated", e.Guild.Id, null, null, correlationId: correlationId);

                await db.SaveChangesAsync();

                var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);

                var beforeIds = e.EmojisBefore.Keys.ToHashSet();
                var afterIds = e.EmojisAfter.Keys.ToHashSet();

                var addedIds = afterIds.Except(beforeIds).ToList();
                var removedIds = beforeIds.Except(afterIds).ToList();
                var updatedIds = beforeIds.Intersect(afterIds)
                    .Where(id => e.EmojisBefore[id].Name != e.EmojisAfter[id].Name)
                    .ToList();

                // Upsert emotes
                foreach (var emoji in e.EmojisAfter.Values)
                {
                    var existing = await db.Emotes
                        .Where(em => em.DiscordId == emoji.Id)
                        .FirstOrDefaultAsync();
                    if (existing == null)
                    {
                        db.Emotes.Add(new EmoteEntity
                        {
                            DiscordId = emoji.Id,
                            GuildId = guildGuid != Guid.Empty ? guildGuid : null,
                            Name = emoji.Name,
                            IsAnimated = emoji.IsAnimated,
                            IsAvailable = emoji.IsAvailable
                        });
                    }
                    else
                    {
                        existing.Name = emoji.Name;
                        existing.IsAnimated = emoji.IsAnimated;
                        existing.IsAvailable = emoji.IsAvailable;
                        existing.IsDeleted = false;
                        existing.DeletedAtUtc = null;
                    }
                }

                // Mark removed as deleted
                foreach (var id in removedIds)
                {
                    await db.Emotes.Where(em => em.DiscordId == id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(em => em.IsDeleted, true)
                            .SetProperty(em => em.DeletedAtUtc, (DateTime?)now));
                }

                var emojiEvent = new EmojiEventEntity
                {
                    GuildDiscordId = e.Guild.Id,
                    EmojisAddedJson = addedIds.Count > 0
                        ? JsonSerializer.Serialize(addedIds.Select(id => new { Id = id, e.EmojisAfter[id].Name }))
                        : null,
                    EmojisRemovedJson = removedIds.Count > 0
                        ? JsonSerializer.Serialize(removedIds.Select(id => new { Id = id, e.EmojisBefore[id].Name }))
                        : null,
                    EmojisUpdatedJson = updatedIds.Count > 0
                        ? JsonSerializer.Serialize(updatedIds.Select(id => new { Id = id, NameBefore = e.EmojisBefore[id].Name, NameAfter = e.EmojisAfter[id].Name }))
                        : null,
                    EventTimestampUtc = now,
                    ReceivedAtUtc = now,
                    RawEventJson = rawJson
                };

                db.EmojiEvents.Add(emojiEvent);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling emojis updated for GuildId={GuildId}", e.Guild.Id);
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "GuildEmojisUpdated", nameof(EmojiEventHandler), ex,
                    e.Guild?.Id, null, null, rawJson, correlationId: correlationId);
            }
        }
    }
}
