using System.Text.Json;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public sealed class StickerEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildStickersUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildStickersUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "GuildStickersUpdated", nameof(StickerEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var guildResult = await guildUpsert.UpsertGuildAsync(e.Guild);
                var guildGuid = guildResult.IsSuccess ? guildResult.Value : (Guid?)null;

                var beforeIds = e.StickersBefore.Keys.ToHashSet();
                var afterIds = e.StickersAfter.Keys.ToHashSet();

                var addedIds = afterIds.Except(beforeIds).ToList();
                var removedIds = beforeIds.Except(afterIds).ToList();
                var updatedIds = beforeIds.Intersect(afterIds)
                    .Where(id => e.StickersBefore[id].Name != e.StickersAfter[id].Name ||
                                 e.StickersBefore[id].Description != e.StickersAfter[id].Description)
                    .ToList();

                foreach (var sticker in e.StickersAfter.Values)
                {
                    var existing = await ctx.Db.Stickers.FirstOrDefaultAsync(s => s.DiscordId == sticker.Id);
                    if (existing == null)
                    {
                        ctx.Db.Stickers.Add(new StickerEntity
                        {
                            DiscordId = sticker.Id,
                            GuildId = guildGuid,
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
                        existing.DeletedAtUtc = null;
                    }
                }

                foreach (var id in removedIds)
                {
                    await ctx.Db.Stickers.Where(s => s.DiscordId == id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(st => st.IsDeleted, true)
                            .SetProperty(st => st.DeletedAtUtc, (DateTime?)ctx.ReceivedAtUtc));
                }

                ctx.Db.StickerEvents.Add(new StickerEventEntity
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
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
