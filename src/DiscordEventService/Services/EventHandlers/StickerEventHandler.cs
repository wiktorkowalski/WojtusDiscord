using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public class StickerEventHandler(IServiceScopeFactory scopeFactory, ILogger<StickerEventHandler> logger) :
    IEventHandler<GuildStickersUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildStickersUpdatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildStickersUpdated", e.Guild.Id, null, null);

            // Look up Guild Guid
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);

            var beforeIds = e.StickersBefore.Keys.ToHashSet();
            var afterIds = e.StickersAfter.Keys.ToHashSet();

            var addedIds = afterIds.Except(beforeIds).ToList();
            var removedIds = beforeIds.Except(afterIds).ToList();
            var updatedIds = beforeIds.Intersect(afterIds)
                .Where(id => e.StickersBefore[id].Name != e.StickersAfter[id].Name ||
                             e.StickersBefore[id].Description != e.StickersAfter[id].Description)
                .ToList();

            // Upsert stickers
            foreach (var sticker in e.StickersAfter.Values)
            {
                var existing = await db.Stickers.FirstOrDefaultAsync(s => s.DiscordId == sticker.Id);
                if (existing == null)
                {
                    db.Stickers.Add(new StickerEntity
                    {
                        DiscordId = sticker.Id,
                        GuildId = guild?.Id,
                        Name = sticker.Name ?? string.Empty,
                        Description = sticker.Description,
                        Tags = sticker.Tags != null ? string.Join(",", sticker.Tags) : null,
                        Type = (int)sticker.Type,
                        FormatType = (int)sticker.FormatType,
                        IsAvailable = true
                    });
                }
                else
                {
                    existing.Name = sticker.Name ?? string.Empty;
                    existing.Description = sticker.Description;
                    existing.Tags = sticker.Tags != null ? string.Join(",", sticker.Tags) : null;
                    existing.IsDeleted = false;
                }
            }

            // Mark removed as deleted
            foreach (var id in removedIds)
            {
                await db.Stickers.Where(s => s.DiscordId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(st => st.IsDeleted, true));
            }

            var stickerEvent = new StickerEventEntity
            {
                GuildDiscordId = e.Guild.Id,
                StickersAddedJson = addedIds.Count > 0
                    ? JsonSerializer.Serialize(addedIds.Select(id => new { Id = id, e.StickersAfter[id].Name }))
                    : null,
                StickersRemovedJson = removedIds.Count > 0
                    ? JsonSerializer.Serialize(removedIds.Select(id => new { Id = id, e.StickersBefore[id].Name }))
                    : null,
                StickersUpdatedJson = updatedIds.Count > 0
                    ? JsonSerializer.Serialize(updatedIds.Select(id => new { Id = id, NameBefore = e.StickersBefore[id].Name, NameAfter = e.StickersAfter[id].Name }))
                    : null,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.StickerEvents.Add(stickerEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling stickers updated for GuildId={GuildId}", e.Guild.Id);
        }
    }
}
