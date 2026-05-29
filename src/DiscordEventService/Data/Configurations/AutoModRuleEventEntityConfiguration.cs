using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class AutoModRuleEventEntityConfiguration : IEntityTypeConfiguration<AutoModRuleEventEntity>
{
    public void Configure(EntityTypeBuilder<AutoModRuleEventEntity> builder)
    {
        builder.HasIndex(a => new { a.GuildDiscordId, a.EventTimestampUtc });

        builder.Property(a => a.ActionsJson).HasColumnType("jsonb");
    }
}
