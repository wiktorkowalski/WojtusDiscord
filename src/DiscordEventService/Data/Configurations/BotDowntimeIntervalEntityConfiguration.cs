using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class BotDowntimeIntervalEntityConfiguration : IEntityTypeConfiguration<BotDowntimeIntervalEntity>
{
    public void Configure(EntityTypeBuilder<BotDowntimeIntervalEntity> builder)
    {
        builder.HasIndex(x => new { x.StartedAtUtc, x.EndedAtUtc });
        builder.HasIndex(x => x.EndedAtUtc)
            .HasFilter("ended_at_utc IS NULL");
    }
}
