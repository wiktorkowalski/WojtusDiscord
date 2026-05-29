using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class BotHeartbeatEntityConfiguration : IEntityTypeConfiguration<BotHeartbeatEntity>
{
    public void Configure(EntityTypeBuilder<BotHeartbeatEntity> builder)
    {
        // Index on LastHeartbeatUtc — append-only table, "most recent tick"
        // lookups via OrderByDescending(LastHeartbeatUtc).First() use this.
        builder.HasIndex(h => h.LastHeartbeatUtc);
    }
}
