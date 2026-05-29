using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class StageInstanceEventEntityConfiguration : IEntityTypeConfiguration<StageInstanceEventEntity>
{
    public void Configure(EntityTypeBuilder<StageInstanceEventEntity> builder)
    {
        builder.HasIndex(s => new { s.GuildDiscordId, s.EventTimestampUtc });
    }
}
