using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class GuildScheduledEventEntityConfiguration : IEntityTypeConfiguration<GuildScheduledEventEntity>
{
    public void Configure(EntityTypeBuilder<GuildScheduledEventEntity> builder)
    {
        builder.HasIndex(e => e.DiscordId).IsUnique();
        builder.HasIndex(e => e.GuildId);
        builder.HasIndex(e => new { e.GuildId, e.IsDeleted });
        builder.ToTable(t => t.HasCheckConstraint("ck_guild_scheduled_events_soft_delete", SoftDeleteConstraint.Check));
    }
}
