using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class PresenceEventEntityConfiguration : IEntityTypeConfiguration<PresenceEventEntity>
{
    public void Configure(EntityTypeBuilder<PresenceEventEntity> builder)
    {
        builder.HasIndex(p => new { p.GuildDiscordId, p.EventTimestampUtc });
        // Composite supports latest-presence-per-user lookups (ORDER BY received_at_utc
        // DESC LIMIT 1, via a backward index scan) in GuildController/PeopleController.
        // Its leading column also serves plain user_discord_id filters, so it replaces
        // the former single-column index rather than adding a redundant one.
        builder.HasIndex(p => new { p.UserDiscordId, p.ReceivedAtUtc });

        builder.Property(p => p.ActivitiesBeforeJson).HasColumnType("jsonb");
        builder.Property(p => p.ActivitiesAfterJson).HasColumnType("jsonb");
    }
}
