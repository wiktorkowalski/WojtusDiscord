using DiscordEventService.Data.Entities.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class UsageAlertEntityConfiguration : IEntityTypeConfiguration<UsageAlertEntity>
{
    public void Configure(EntityTypeBuilder<UsageAlertEntity> builder)
    {
        builder.ToTable("usage_alerts");

        // The dedup key (#269): insert-if-absent on this index is what makes each
        // threshold alert fire once per window and limit (#453: a raised cap re-arms).
        // NULLS NOT DISTINCT so the global caps (user null) dedup too — plain unique
        // would treat every null as new. Explicit name: the default would exceed Postgres' 63 chars.
        builder.HasIndex(a => new { a.Cap, a.WindowStartUtc, a.Level, a.UserDiscordId, a.LimitUsd })
            .HasDatabaseName("ix_usage_alerts_dedup")
            .IsUnique()
            .AreNullsDistinct(false);
    }
}
