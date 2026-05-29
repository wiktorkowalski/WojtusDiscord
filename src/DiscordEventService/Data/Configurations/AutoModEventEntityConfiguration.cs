using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class AutoModEventEntityConfiguration : IEntityTypeConfiguration<AutoModEventEntity>
{
    public void Configure(EntityTypeBuilder<AutoModEventEntity> builder)
    {
        builder.HasIndex(a => new { a.GuildDiscordId, a.EventTimestampUtc });
        builder.HasIndex(a => a.RuleDiscordId);
    }
}
