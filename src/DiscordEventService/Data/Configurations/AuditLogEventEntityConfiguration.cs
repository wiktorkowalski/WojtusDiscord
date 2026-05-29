using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class AuditLogEventEntityConfiguration : IEntityTypeConfiguration<AuditLogEventEntity>
{
    public void Configure(EntityTypeBuilder<AuditLogEventEntity> builder)
    {
        builder.HasIndex(a => new { a.GuildDiscordId, a.EventTimestampUtc });
        builder.HasIndex(a => a.UserDiscordId);
        builder.HasIndex(a => a.ActionType);

        builder.Property(a => a.ChangesJson).HasColumnType("jsonb");
        builder.Property(a => a.OptionsJson).HasColumnType("jsonb");
    }
}
