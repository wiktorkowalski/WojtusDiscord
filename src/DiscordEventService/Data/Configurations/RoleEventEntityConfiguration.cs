using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class RoleEventEntityConfiguration : IEntityTypeConfiguration<RoleEventEntity>
{
    public void Configure(EntityTypeBuilder<RoleEventEntity> builder)
    {
        builder.HasIndex(r => new { r.GuildDiscordId, r.EventTimestampUtc });
    }
}
