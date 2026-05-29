using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class InviteEventEntityConfiguration : IEntityTypeConfiguration<InviteEventEntity>
{
    public void Configure(EntityTypeBuilder<InviteEventEntity> builder)
    {
        builder.HasIndex(i => new { i.GuildDiscordId, i.EventTimestampUtc });
    }
}
