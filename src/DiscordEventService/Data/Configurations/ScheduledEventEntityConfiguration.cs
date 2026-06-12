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

        // Properties carry the Utc suffix convention; the columns predate it.
        builder.Property(s => s.ScheduledStartTimeUtc).HasColumnName("scheduled_start_time");
        builder.Property(s => s.ScheduledEndTimeUtc).HasColumnName("scheduled_end_time");
    }
}
