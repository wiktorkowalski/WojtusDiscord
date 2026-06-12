using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class RawEventLogEntityConfiguration : IEntityTypeConfiguration<RawEventLogEntity>
{
    public void Configure(EntityTypeBuilder<RawEventLogEntity> builder)
    {
        builder.HasIndex(r => new { r.GuildDiscordId, r.ReceivedAtUtc });
        builder.HasIndex(r => r.EventType);
        builder.HasIndex(r => r.ReceivedAtUtc);
        builder.HasIndex(r => r.UserDiscordId);

        builder.Property(r => r.EventJson).HasColumnType("jsonb");
        builder.Property(r => r.EventType).HasMaxLength(100);

        // Property carries the Is- prefix convention; the column predates it.
        builder.Property(r => r.IsSerializationFailed).HasColumnName("serialization_failed");
    }
}
