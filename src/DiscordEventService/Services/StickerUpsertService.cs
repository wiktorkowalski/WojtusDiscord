using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;

namespace DiscordEventService.Services;

// The single write seam for sticker rows (#290): live GuildStickersUpdated and the stickers
// backfill both go through UpsertStickerAsync so the column map exists exactly once.
internal sealed class StickerUpsertService(DiscordDbContext db, ILogger<StickerUpsertService> logger)
{
    public async Task<UpsertResult<Guid>> UpsertStickerAsync(
        DiscordMessageSticker sticker, Guid? guildId, CancellationToken cancellationToken = default)
    {
        var name = sticker.Name ?? string.Empty;
        var tags = sticker.Tags is not null ? string.Join(",", sticker.Tags) : null;

        // A live sighting means the sticker exists — always clear soft-deletion. GuildId heals
        // (COALESCE) when the guild resolved and stays untouched when it didn't, so a row
        // inserted during a transient guild miss picks up its FK on the next sighting.
        var id = await db.Stickers.UpsertAsync(
            s => s.DiscordId == sticker.Id,
            s => s
                .SetProperty(st => st.GuildId, st => guildId ?? st.GuildId)
                .SetProperty(st => st.PackId, sticker.PackId)
                .SetProperty(st => st.Name, name)
                .SetProperty(st => st.Description, sticker.Description)
                .SetProperty(st => st.Tags, tags)
                .SetProperty(st => st.Type, (int)sticker.Type)
                .SetProperty(st => st.FormatType, (int)sticker.FormatType)
                .SetProperty(st => st.IsAvailable, sticker.Available)
                .SetProperty(st => st.IsDeleted, false)
                .SetProperty(st => st.DeletedAtUtc, (DateTime?)null),
            () => new StickerEntity
            {
                DiscordId = sticker.Id,
                GuildId = guildId,
                PackId = sticker.PackId,
                Name = name,
                Description = sticker.Description,
                Tags = tags,
                Type = (int)sticker.Type,
                FormatType = (int)sticker.FormatType,
                IsAvailable = sticker.Available,
                IsDeleted = false
            },
            s => s.Id,
            cancellationToken);

        if (id == Guid.Empty)
        {
            logger.LogError("Sticker upsert lost the row for sticker {StickerId} after upsert", sticker.Id);
            return UpsertResult<Guid>.Failure($"Sticker upsert lost the row for DiscordId={sticker.Id}");
        }

        return UpsertResult<Guid>.Success(id);
    }
}
