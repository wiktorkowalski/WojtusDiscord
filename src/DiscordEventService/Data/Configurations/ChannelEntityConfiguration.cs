using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class ChannelEntityConfiguration : IEntityTypeConfiguration<ChannelEntity>
{
    public void Configure(EntityTypeBuilder<ChannelEntity> builder)
    {
        builder.HasIndex(c => c.DiscordId).IsUnique();
        builder.HasIndex(c => c.GuildId);
        builder.ToTable(t => t.HasCheckConstraint("ck_channels_soft_delete", SoftDeleteConstraint.Check));
    }
}
