using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class VoiceStateEventEntityConfiguration : IEntityTypeConfiguration<VoiceStateEventEntity>
{
    public void Configure(EntityTypeBuilder<VoiceStateEventEntity> builder)
    {
        builder.HasIndex(v => new { v.GuildDiscordId, v.EventTimestampUtc });
        builder.HasIndex(v => v.UserDiscordId);
    }
}
