using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class TypingEventEntityConfiguration : IEntityTypeConfiguration<TypingEventEntity>
{
    public void Configure(EntityTypeBuilder<TypingEventEntity> builder)
    {
        builder.HasIndex(t => new { t.GuildDiscordId, t.ReceivedAtUtc });
        builder.HasIndex(t => t.UserDiscordId);

        // Property carries the Utc suffix convention; the column predates it.
        builder.Property(t => t.StartedAtUtc).HasColumnName("started_at");
    }
}
