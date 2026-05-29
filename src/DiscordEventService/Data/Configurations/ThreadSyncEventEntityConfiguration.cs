using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ThreadSyncEventEntityConfiguration : IEntityTypeConfiguration<ThreadSyncEventEntity>
{
    public void Configure(EntityTypeBuilder<ThreadSyncEventEntity> builder)
    {
        builder.HasIndex(t => new { t.GuildDiscordId, t.EventTimestampUtc });

        builder.Property(t => t.ThreadIdsJson).HasColumnType("jsonb");
        builder.Property(t => t.ChannelIdsJson).HasColumnType("jsonb");
        builder.Property(t => t.MembersJson).HasColumnType("jsonb");
    }
}
