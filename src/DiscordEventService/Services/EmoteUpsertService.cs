using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;

namespace DiscordEventService.Services;

// The single write seam for emote rows (#290): live GuildEmojisUpdated and the emojis
// backfill both go through UpsertEmoteAsync so the column map exists exactly once.
internal sealed class EmoteUpsertService(DiscordDbContext db, ILogger<EmoteUpsertService> logger)
{
    public async Task<UpsertResult<Guid>> UpsertEmoteAsync(
        DiscordEmoji emoji, Guid? guildId, CancellationToken cancellationToken = default)
    {
        // A live sighting means the emote exists — always clear soft-deletion. GuildId heals
        // (COALESCE) when the guild resolved and stays untouched when it didn't, so a row
        // inserted during a transient guild miss picks up its FK on the next sighting.
        var id = await db.Emotes.UpsertAsync(
            e => e.DiscordId == emoji.Id,
            s => s
                .SetProperty(e => e.GuildId, e => guildId ?? e.GuildId)
                .SetProperty(e => e.Name, emoji.Name)
                .SetProperty(e => e.IsAnimated, emoji.IsAnimated)
                .SetProperty(e => e.IsAvailable, emoji.IsAvailable)
                .SetProperty(e => e.IsDeleted, false)
                .SetProperty(e => e.DeletedAtUtc, (DateTime?)null),
            () => new EmoteEntity
            {
                DiscordId = emoji.Id,
                GuildId = guildId,
                Name = emoji.Name,
                IsAnimated = emoji.IsAnimated,
                IsAvailable = emoji.IsAvailable,
                IsDeleted = false
            },
            e => e.Id,
            cancellationToken);

        if (id == Guid.Empty)
        {
            logger.LogError("Emote upsert lost the row for emote {EmoteId} after upsert", emoji.Id);
            return UpsertResult<Guid>.Failure($"Emote upsert lost the row for DiscordId={emoji.Id}");
        }

        return UpsertResult<Guid>.Success(id);
    }
}
