using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordEventService.Data.Configurations;

internal sealed class StageInstanceEntityConfiguration : IEntityTypeConfiguration<StageInstanceEntity>
{
    public void Configure(EntityTypeBuilder<StageInstanceEntity> builder)
    {
        builder.HasIndex(s => s.DiscordId).IsUnique();
        builder.HasIndex(s => s.GuildId);
        builder.HasIndex(s => s.ChannelId);
        builder.ToTable(t => t.HasCheckConstraint("ck_stage_instances_soft_delete", SoftDeleteConstraint.Check));
    }
}
