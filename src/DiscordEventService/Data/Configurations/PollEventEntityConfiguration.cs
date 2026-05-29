using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class PollEventEntityConfiguration : IEntityTypeConfiguration<PollEventEntity>
{
    public void Configure(EntityTypeBuilder<PollEventEntity> builder)
    {
        builder.HasIndex(p => new { p.GuildDiscordId, p.EventTimestampUtc });
        builder.HasIndex(p => p.MessageDiscordId);
    }
}
