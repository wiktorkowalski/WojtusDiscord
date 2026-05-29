using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class PinEventEntityConfiguration : IEntityTypeConfiguration<PinEventEntity>
{
    public void Configure(EntityTypeBuilder<PinEventEntity> builder)
    {
        builder.HasIndex(p => new { p.GuildDiscordId, p.EventTimestampUtc });
        builder.HasIndex(p => p.ChannelDiscordId);
    }
}
