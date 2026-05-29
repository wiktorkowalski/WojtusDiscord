using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class StickerEventEntityConfiguration : IEntityTypeConfiguration<StickerEventEntity>
{
    public void Configure(EntityTypeBuilder<StickerEventEntity> builder)
    {
        builder.HasIndex(s => new { s.GuildDiscordId, s.EventTimestampUtc });

        builder.Property(s => s.StickersAddedJson).HasColumnType("jsonb");
        builder.Property(s => s.StickersRemovedJson).HasColumnType("jsonb");
        builder.Property(s => s.StickersUpdatedJson).HasColumnType("jsonb");
    }
}
