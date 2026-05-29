using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ReactionEventEntityConfiguration : IEntityTypeConfiguration<ReactionEventEntity>
{
    public void Configure(EntityTypeBuilder<ReactionEventEntity> builder)
    {
        builder.HasIndex(r => new { r.GuildDiscordId, r.EventTimestampUtc });
        builder.HasIndex(r => r.MessageDiscordId);
    }
}
