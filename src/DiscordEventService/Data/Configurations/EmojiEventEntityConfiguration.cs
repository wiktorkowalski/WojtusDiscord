using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class EmojiEventEntityConfiguration : IEntityTypeConfiguration<EmojiEventEntity>
{
    public void Configure(EntityTypeBuilder<EmojiEventEntity> builder)
    {
        builder.HasIndex(e => new { e.GuildDiscordId, e.EventTimestampUtc });

        builder.Property(e => e.EmojisAddedJson).HasColumnType("jsonb");
        builder.Property(e => e.EmojisRemovedJson).HasColumnType("jsonb");
        builder.Property(e => e.EmojisUpdatedJson).HasColumnType("jsonb");
    }
}
