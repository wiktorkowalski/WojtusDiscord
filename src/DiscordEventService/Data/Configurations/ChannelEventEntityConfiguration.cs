using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ChannelEventEntityConfiguration : IEntityTypeConfiguration<ChannelEventEntity>
{
    public void Configure(EntityTypeBuilder<ChannelEventEntity> builder)
    {
        builder.HasIndex(c => new { c.GuildDiscordId, c.EventTimestampUtc });
        builder.HasIndex(c => c.ChannelDiscordId);
    }
}
