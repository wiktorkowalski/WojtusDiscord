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
    }
}
