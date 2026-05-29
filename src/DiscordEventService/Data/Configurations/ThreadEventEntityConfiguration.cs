using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ThreadEventEntityConfiguration : IEntityTypeConfiguration<ThreadEventEntity>
{
    public void Configure(EntityTypeBuilder<ThreadEventEntity> builder)
    {
        builder.HasIndex(t => new { t.GuildDiscordId, t.EventTimestampUtc });
        builder.HasIndex(t => t.ParentChannelDiscordId);

        builder.Property(t => t.MembersAddedJson).HasColumnType("jsonb");
        builder.Property(t => t.MembersRemovedJson).HasColumnType("jsonb");
    }
}
