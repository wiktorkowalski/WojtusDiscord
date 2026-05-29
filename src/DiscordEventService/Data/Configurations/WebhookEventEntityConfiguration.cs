using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class WebhookEventEntityConfiguration : IEntityTypeConfiguration<WebhookEventEntity>
{
    public void Configure(EntityTypeBuilder<WebhookEventEntity> builder)
    {
        builder.HasIndex(w => new { w.GuildDiscordId, w.EventTimestampUtc });
    }
}
