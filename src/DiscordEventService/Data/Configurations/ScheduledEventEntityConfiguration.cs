using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ScheduledEventEntityConfiguration : IEntityTypeConfiguration<ScheduledEventEntity>
{
    public void Configure(EntityTypeBuilder<ScheduledEventEntity> builder)
    {
        builder.HasIndex(s => new { s.GuildDiscordId, s.EventTimestampUtc });
        builder.HasIndex(s => s.EventDiscordId);
    }
}
