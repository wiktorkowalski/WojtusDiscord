using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class IntegrationEventEntityConfiguration : IEntityTypeConfiguration<IntegrationEventEntity>
{
    public void Configure(EntityTypeBuilder<IntegrationEventEntity> builder)
    {
        builder.HasIndex(i => new { i.GuildDiscordId, i.EventTimestampUtc });
    }
}
