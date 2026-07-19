using System.Text.Json;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class StickerEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildStickersUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildStickersUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildStickersUpdated", nameof(StickerEventHandler),
            e.Guild.Id, null, null, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var stickerUpsert = ctx.Services.GetRequiredService<StickerUpsertService>();
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

                // Sticker rows + event row must commit together. ExecutionStrategy is required
                // because EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // Reset the tracker so an EnableRetryOnFailure retry doesn't re-stage entities
                    // left Added by a rolled-back attempt.
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    foreach (var sticker in e.StickersAfter.Values)
                        await stickerUpsert.UpsertStickerAsync(sticker, guildGuid);

                    await SoftDeleteRemovedStickersAsync(ctx, removedIds);

                    ctx.Db.StickerEvents.Add(BuildStickerEvent(e, ctx, addedIds, removedIds, updatedIds));
                    await ctx.Db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            });
    }

    private static async Task SoftDeleteRemovedStickersAsync(EventContext ctx, List<ulong> removedIds)
    {
        foreach (var id in removedIds)
        {
            await ctx.Db.Stickers.Where(s => s.DiscordId == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(st => st.IsDeleted, true)
                    .SetProperty(st => st.DeletedAtUtc, (DateTime?)ctx.ReceivedAtUtc));
        }
    }

    private static StickerEventEntity BuildStickerEvent(
        GuildStickersUpdatedEventArgs e,
        EventContext ctx,
        List<ulong> addedIds,
        List<ulong> removedIds,
        List<ulong> updatedIds) => new StickerEventEntity
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
        };
}
