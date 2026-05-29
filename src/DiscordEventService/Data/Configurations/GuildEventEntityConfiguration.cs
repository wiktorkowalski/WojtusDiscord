using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class GuildEventEntityConfiguration : IEntityTypeConfiguration<GuildEventEntity>
{
    public void Configure(EntityTypeBuilder<GuildEventEntity> builder)
    {
        builder.HasIndex(g => new { g.GuildDiscordId, g.EventTimestampUtc });
    }
}
