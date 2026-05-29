using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class FailedEventEntityConfiguration : IEntityTypeConfiguration<FailedEventEntity>
{
    public void Configure(EntityTypeBuilder<FailedEventEntity> builder)
    {
        builder.HasIndex(f => f.IsResolved);
        builder.HasIndex(f => f.FailedAtUtc);
        builder.HasIndex(f => new { f.IsResolved, f.FailedAtUtc });
        builder.HasIndex(f => f.EventType);
        builder.HasIndex(f => f.GuildDiscordId);
    }
}
