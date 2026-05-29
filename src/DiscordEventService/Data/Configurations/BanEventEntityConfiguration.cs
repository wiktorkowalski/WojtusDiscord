using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class BanEventEntityConfiguration : IEntityTypeConfiguration<BanEventEntity>
{
    public void Configure(EntityTypeBuilder<BanEventEntity> builder)
    {
        builder.HasIndex(b => new { b.GuildDiscordId, b.EventTimestampUtc });
        builder.HasIndex(b => b.UserDiscordId);
    }
}
