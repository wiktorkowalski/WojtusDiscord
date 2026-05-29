using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class VoiceServerEventEntityConfiguration : IEntityTypeConfiguration<VoiceServerEventEntity>
{
    public void Configure(EntityTypeBuilder<VoiceServerEventEntity> builder)
    {
        builder.HasIndex(v => new { v.GuildDiscordId, v.EventTimestampUtc });
    }
}
