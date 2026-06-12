using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class BotHeartbeatEntityConfiguration : IEntityTypeConfiguration<BotHeartbeatEntity>
{
    // Index on LastHeartbeatUtc — append-only table, "most recent tick"
    // lookups via OrderByDescending(LastHeartbeatUtc).First() use this.
    public void Configure(EntityTypeBuilder<BotHeartbeatEntity> builder) =>
        builder.HasIndex(h => h.LastHeartbeatUtc);
}
