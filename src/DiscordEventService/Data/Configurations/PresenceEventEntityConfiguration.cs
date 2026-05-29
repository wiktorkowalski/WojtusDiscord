using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class PresenceEventEntityConfiguration : IEntityTypeConfiguration<PresenceEventEntity>
{
    public void Configure(EntityTypeBuilder<PresenceEventEntity> builder)
    {
        builder.HasIndex(p => new { p.GuildDiscordId, p.EventTimestampUtc });
        builder.HasIndex(p => p.UserDiscordId);

        builder.Property(p => p.ActivitiesBeforeJson).HasColumnType("jsonb");
        builder.Property(p => p.ActivitiesAfterJson).HasColumnType("jsonb");
    }
}
